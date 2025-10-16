using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.RPi.Exports;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Tests.TestHelpers;
using HVO.SkyMonitorV5.RPi.Telemetry;
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

    [TestMethod]
    public async Task DispatchAsync_OnSuccess_RecordsTelemetryWithMetadata()
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
        var nowUtc = new DateTimeOffset(2025, 10, 13, 6, 45, 0, TimeSpan.Zero);
        clock.SetupGet(c => c.UtcNow).Returns(() => nowUtc);
        clock.SetupGet(c => c.LocalNow).Returns(() => nowUtc);
        clock.SetupGet(c => c.TimeZone).Returns(TimeZoneInfo.Utc);
        clock.SetupGet(c => c.TimeZoneDisplayName).Returns("UTC");
        clock.Setup(c => c.ToLocal(It.IsAny<DateTimeOffset>())).Returns<DateTimeOffset>(timestamp => timestamp);
        clock.Setup(c => c.GetZoneLabel(It.IsAny<DateTimeOffset>())).Returns("UTC");

        var frameId = Guid.NewGuid();
        var capturedAt = new DateTimeOffset(2025, 10, 13, 6, 30, 0, TimeSpan.Zero);
        var metadata = new FrameExportMetadata(
            frameId,
            capturedAt,
            capturedAt,
            new ExposureSettings(1000, 220, false, false),
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
            7.5,
            12.3,
            1020.4,
            RawImageDescriptor: null,
            PayloadContentType: "application/vnd.hvo.skia.raw",
            PayloadExtension: "skimg");

        var payload = new ReadOnlyMemory<byte>(new byte[] { 4, 5, 6, 7 });
        var envelope = new FrameExportEnvelope(
            frameId,
            FrameExportStage.Raw,
            metadata,
            payload,
            "application/vnd.hvo.skia.raw",
            "skimg");

        var sinkCompletion = new TaskCompletionSource<FrameExportEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        var telemetryCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        const string sinkName = "raw-filesystem";
        var sink = new SuccessfulSink(sinkName, FrameExportStage.Raw, sinkCompletion);

        var telemetry = new Mock<ISkyMonitorTelemetryRecorder>(MockBehavior.Strict);
        telemetry
            .Setup(recorder => recorder.RecordFrameExportAttempt(
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                frameId,
                FrameExportStage.Raw,
                sinkName,
                true,
                It.Is<double?>(value => !value.HasValue || value >= 0),
                It.Is<long?>(value => value == payload.Length),
                metadata.PayloadContentType,
                metadata.PayloadExtension,
                metadata.QueueLatencyMilliseconds,
                metadata.ProcessingMilliseconds,
                metadata.FullPipelineMilliseconds,
                metadata.FramesStacked,
                metadata.IntegrationMilliseconds,
                null))
            .Callback(() => telemetryCompletion.TrySetResult(true))
            .Verifiable();

        var dispatcher = new FrameExportDispatcher(
            Microsoft.Extensions.Options.Options.Create(dispatcherOptions),
            new[] { sink },
            clock.Object,
            telemetry.Object,
            metrics,
            retryQueue: null,
            NullLogger<FrameExportDispatcher>.Instance);

        await dispatcher.StartAsync(CancellationToken.None).ConfigureAwait(false);

        await dispatcher.EnqueueAsync(envelope, CancellationToken.None).ConfigureAwait(false);

        var completion = Task.WhenAll(sinkCompletion.Task, telemetryCompletion.Task);
        var finished = await Task.WhenAny(completion, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
        Assert.AreEqual(completion, finished, "Dispatcher did not complete sink + telemetry callbacks in time.");

        var invokedEnvelope = await sinkCompletion.Task.ConfigureAwait(false);
        Assert.AreEqual(frameId, invokedEnvelope.FrameId, "Sink should receive the enqueued frame id.");

        telemetry.VerifyAll();

        await dispatcher.StopAsync(CancellationToken.None).ConfigureAwait(false);
        dispatcher.Dispose();
    }

    [TestMethod]
    public async Task DispatchAsync_WhenSinkRecovers_AcknowledgesOutstandingRetry()
    {
        var dispatcherOptions = new FrameExportDispatcherOptions
        {
            ChannelCapacity = 2,
            MaxConcurrency = 1,
            DrainTimeout = TimeSpan.FromSeconds(1)
        };

        using var meterFactory = new TestMeterFactory();
        using var metrics = new FrameExportMetrics(meterFactory);

        var clock = new Mock<IObservatoryClock>();
        clock.SetupGet(c => c.UtcNow).Returns(() => DateTimeOffset.UtcNow);
        clock.SetupGet(c => c.LocalNow).Returns(() => DateTimeOffset.UtcNow);
        clock.SetupGet(c => c.TimeZone).Returns(TimeZoneInfo.Utc);
        clock.SetupGet(c => c.TimeZoneDisplayName).Returns("UTC");
        clock.Setup(c => c.ToLocal(It.IsAny<DateTimeOffset>())).Returns<DateTimeOffset>(timestamp => timestamp);
        clock.Setup(c => c.GetZoneLabel(It.IsAny<DateTimeOffset>())).Returns("UTC");

        var frameId = Guid.NewGuid();
        var capturedAt = DateTimeOffset.UtcNow;

        var metadata = new FrameExportMetadata(
            frameId,
            capturedAt,
            capturedAt,
            new ExposureSettings(500, 200, false, false),
            "Rig",
            "Camera",
            "Lens",
            35.0,
            -114.0,
            false,
            null,
            false,
            1,
            500,
            AppliedFilters: Array.Empty<string>(),
            QueueLatencyMilliseconds: 2.5,
            ProcessingMilliseconds: null,
            FullPipelineMilliseconds: null,
            RawImageDescriptor: null,
            PayloadContentType: "application/vnd.hvo.skia.raw",
            PayloadExtension: "skimg");

        var envelope = new FrameExportEnvelope(
            frameId,
            FrameExportStage.Raw,
            metadata,
            new ReadOnlyMemory<byte>(new byte[] { 9, 8, 7 }),
            "application/vnd.hvo.skia.raw",
            "skimg");

    var retryQueue = new Mock<IFrameExportRetryQueue>(MockBehavior.Strict);
    var scheduledRetries = new List<FrameExportRetryRequest>();
    var syncRoot = new object();

        retryQueue
            .Setup(queue => queue.ScheduleRetryAsync(It.IsAny<FrameExportRetryRequest>(), It.IsAny<CancellationToken>()))
            .Returns<FrameExportRetryRequest, CancellationToken>((request, _) =>
            {
                lock (syncRoot)
                {
                    scheduledRetries.Add(request);
                }
                return ValueTask.CompletedTask;
            });

        var sink = new FlakySink("raw-flaky", FrameExportStage.Raw, failAttempts: 1);

        var dispatcher = new FrameExportDispatcher(
            Microsoft.Extensions.Options.Options.Create(dispatcherOptions),
            new[] { sink },
            clock.Object,
            telemetryRecorder: null,
            metrics,
            retryQueue.Object,
            NullLogger<FrameExportDispatcher>.Instance);

        await dispatcher.StartAsync(CancellationToken.None).ConfigureAwait(false);

        await dispatcher.EnqueueAsync(envelope, CancellationToken.None).ConfigureAwait(false);

        await WaitForRetryCountAsync(1, TimeSpan.FromSeconds(2)).ConfigureAwait(false);

    sink.AllowSuccess();

    // simulate retry replay by enqueuing the envelope again; dispatcher should treat as success
    await dispatcher.EnqueueAsync(envelope, CancellationToken.None).ConfigureAwait(false);

        await Task.Delay(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);

        lock (syncRoot)
        {
            Assert.AreEqual(1, scheduledRetries.Count, "Original retry should remain scheduled until external completion.");
        }

        retryQueue.Verify(queue => queue.ScheduleRetryAsync(It.IsAny<FrameExportRetryRequest>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce());

        await dispatcher.StopAsync(CancellationToken.None).ConfigureAwait(false);
        dispatcher.Dispose();

        async Task WaitForRetryCountAsync(int expected, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow <= deadline)
            {
                lock (syncRoot)
                {
                    if (scheduledRetries.Count >= expected)
                    {
                        return;
                    }
                }

                await Task.Delay(50).ConfigureAwait(false);
            }

            lock (syncRoot)
            {
                Assert.Fail(FormattableString.Invariant($"Expected retry count {expected}, actual {scheduledRetries.Count} within timeout."));
            }
        }
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

    private sealed class SuccessfulSink : IFrameExportSink
    {
        private readonly string _name;
        private readonly FrameExportStage _stage;
        private readonly TaskCompletionSource<FrameExportEnvelope> _completion;

        public SuccessfulSink(string name, FrameExportStage stage, TaskCompletionSource<FrameExportEnvelope> completion)
        {
            _name = name;
            _stage = stage;
            _completion = completion;
        }

        public string Name => _name;

        public bool SupportsStage(FrameExportStage stage) => stage == _stage;

        public ValueTask<Result<bool>> ExportAsync(FrameExportEnvelope envelope, CancellationToken cancellationToken)
        {
            _completion.TrySetResult(envelope);
            return ValueTask.FromResult(Result<bool>.Success(true));
        }
    }

    private sealed class FlakySink : IFrameExportSink
    {
        private readonly string _name;
        private readonly FrameExportStage _stage;
        private int _remainingFailures;
        private volatile bool _allowSuccess;

        public FlakySink(string name, FrameExportStage stage, int failAttempts)
        {
            _name = name;
            _stage = stage;
            _remainingFailures = failAttempts;
        }

        public string Name => _name;

        public bool SupportsStage(FrameExportStage stage) => stage == _stage;

        public void AllowSuccess() => _allowSuccess = true;

        public ValueTask<Result<bool>> ExportAsync(FrameExportEnvelope envelope, CancellationToken cancellationToken)
        {
            if (!_allowSuccess && Interlocked.Decrement(ref _remainingFailures) >= 0)
            {
                return ValueTask.FromResult(Result<bool>.Success(false));
            }

            return ValueTask.FromResult(Result<bool>.Success(true));
        }
    }
}
