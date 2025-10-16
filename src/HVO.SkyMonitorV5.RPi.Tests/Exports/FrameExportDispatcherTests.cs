using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.RPi.Exports;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace HVO.SkyMonitorV5.RPi.Tests.Exports;

[TestClass]
public sealed class FrameExportDispatcherTests
{
    [TestMethod]
    public async Task DispatchAsync_SchedulesRetryWhenSinkReportsFailure()
    {
        var dispatcherOptions = new FrameExportDispatcherOptions
        {
            ChannelCapacity = 4,
            MaxConcurrency = 1,
            DrainTimeout = TimeSpan.FromSeconds(1)
        };

        using var meterFactory = new TestMeterFactory();
        using var metrics = new FrameExportMetrics(meterFactory);

        var clock = new Mock<IObservatoryClock>();
        clock.SetupGet(c => c.UtcNow).Returns(() => DateTimeOffset.UtcNow);
        clock.SetupGet(c => c.LocalNow).Returns(() => DateTimeOffset.Now);
        clock.SetupGet(c => c.TimeZone).Returns(TimeZoneInfo.Utc);
        clock.SetupGet(c => c.TimeZoneDisplayName).Returns("UTC");
        clock.Setup(c => c.ToLocal(It.IsAny<DateTimeOffset>())).Returns<DateTimeOffset>(timestamp => timestamp.ToLocalTime());
        clock.Setup(c => c.GetZoneLabel(It.IsAny<DateTimeOffset>())).Returns("UTC");

        var retryQueue = new Mock<IFrameExportRetryQueue>();
        var retryScheduled = new TaskCompletionSource<FrameExportRetryRequest>(TaskCreationOptions.RunContinuationsAsynchronously);

        retryQueue
            .Setup(queue => queue.ScheduleRetryAsync(It.IsAny<FrameExportRetryRequest>(), It.IsAny<CancellationToken>()))
            .Returns<FrameExportRetryRequest, CancellationToken>((request, _) =>
            {
                retryScheduled.TrySetResult(request);
                return ValueTask.CompletedTask;
            });

        var sink = new FailingSink("raw-s3", FrameExportStage.Raw);
        var dispatcher = new FrameExportDispatcher(
            Microsoft.Extensions.Options.Options.Create(dispatcherOptions),
            new[] { sink },
            clock.Object,
            telemetryRecorder: null,
            metrics,
            retryQueue.Object,
            NullLogger<FrameExportDispatcher>.Instance);

        await dispatcher.StartAsync(CancellationToken.None).ConfigureAwait(false);

        var frameId = Guid.NewGuid();
        var capturedAt = new DateTimeOffset(2025, 10, 13, 6, 30, 0, TimeSpan.Zero);
        var metadata = new FrameExportMetadata(
            frameId,
            capturedAt,
            capturedAt,
            new ExposureSettings(1000, 200, false, false),
            "Rig",
            "Camera",
            "Lens",
            35.0,
            -114.0,
            false,
            null,
            false,
            1,
            1000,
            new List<string> { "Filter" },
            5.0,
            10.0,
            1015.0,
            RawImageDescriptor: null,
            PayloadContentType: "application/vnd.hvo.skia.raw",
            PayloadExtension: "skimg");

        var envelope = new FrameExportEnvelope(
            frameId,
            FrameExportStage.Raw,
            metadata,
            new ReadOnlyMemory<byte>(new byte[] { 1, 2, 3 }),
            "application/vnd.hvo.skia.raw",
            "skimg");

        await dispatcher.EnqueueAsync(envelope, CancellationToken.None).ConfigureAwait(false);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var completedTask = await Task.WhenAny(retryScheduled.Task, Task.Delay(Timeout.InfiniteTimeSpan, timeoutCts.Token)).ConfigureAwait(false);
        Assert.AreEqual(retryScheduled.Task, completedTask, "Retry schedule callback was not invoked in time.");

        var request = await retryScheduled.Task.ConfigureAwait(false);
        Assert.AreEqual(frameId, request.Envelope.FrameId, "Retry request should carry the original frame id.");
        Assert.AreEqual(FrameExportStage.Raw, request.Envelope.Stage, "Retry request should retain the frame stage.");
        Assert.AreEqual("raw-s3", request.SinkName, "Retry request should target the failing sink name.");
        Assert.AreEqual(1, request.AttemptCount, "Initial retry request should record attempt count of one.");
        Assert.AreEqual("Sink reported unsuccessful result.", request.ErrorMessage, "Retry request should include dispatcher failure message.");

        retryQueue.Verify(queue => queue.ScheduleRetryAsync(It.IsAny<FrameExportRetryRequest>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce());

        await dispatcher.StopAsync(CancellationToken.None).ConfigureAwait(false);
        dispatcher.Dispose();
    }

    private sealed class FailingSink : IFrameExportSink
    {
        private readonly string _name;
        private readonly FrameExportStage _stage;
        private int _callCount;

        public FailingSink(string name, FrameExportStage stage)
        {
            _name = name;
            _stage = stage;
        }

        public string Name => _name;

        public int CallCount => Volatile.Read(ref _callCount);

        public bool SupportsStage(FrameExportStage stage) => stage == _stage;

        public ValueTask<Result<bool>> ExportAsync(FrameExportEnvelope envelope, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            return ValueTask.FromResult(Result<bool>.Success(false));
        }
    }
}
