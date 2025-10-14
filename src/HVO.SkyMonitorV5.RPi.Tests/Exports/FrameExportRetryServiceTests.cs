using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.Data.Telemetry;
using HVO.SkyMonitorV5.Data.Telemetry.Entities;
using HVO.SkyMonitorV5.RPi.Exports;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace HVO.SkyMonitorV5.RPi.Tests.Exports;

[TestClass]
public sealed class FrameExportRetryServiceTests
{
    [TestMethod]
    public async Task RetryService_PersistsFailedEnvelopeAndReplaysUntilSuccess()
    {
        var retryOptions = new FrameExportRetryOptions
        {
            Enabled = true,
            MaxAttempts = 5,
            InitialBackoff = TimeSpan.FromMilliseconds(25),
            BackoffMultiplier = 1.0,
            MaxBackoff = TimeSpan.FromMilliseconds(50),
            MaxJitter = TimeSpan.Zero,
            MaxQueueSize = 10,
            BatchSize = 2,
            PollInterval = TimeSpan.FromMilliseconds(10)
        };

        using var optionsMonitor = new TestOptionsMonitor<FrameExportRetryOptions>(retryOptions);
        using var meterFactory = new TestMeterFactory();
        using var metrics = new FrameExportMetrics(meterFactory);

        var dbName = Guid.NewGuid().ToString("N");
        var dbOptions = new DbContextOptionsBuilder<SkyMonitorTelemetryContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var contextFactory = new TestDbContextFactory<SkyMonitorTelemetryContext>(() =>
        {
            var context = new SkyMonitorTelemetryContext(dbOptions);
            context.Database.EnsureCreated();
            return context;
        });

        var clock = new Mock<IObservatoryClock>();
        clock.SetupGet(c => c.UtcNow).Returns(() => DateTimeOffset.UtcNow);
        clock.SetupGet(c => c.LocalNow).Returns(() => DateTimeOffset.Now);
        clock.SetupGet(c => c.TimeZone).Returns(TimeZoneInfo.Utc);
        clock.SetupGet(c => c.TimeZoneDisplayName).Returns("UTC");
        clock.Setup(c => c.ToLocal(It.IsAny<DateTimeOffset>())).Returns<DateTimeOffset>(timestamp => timestamp.ToLocalTime());
        clock.Setup(c => c.GetZoneLabel(It.IsAny<DateTimeOffset>())).Returns("UTC");

        var sink = new TestRetrySink(
            "processed-s3",
            FrameExportStage.Processed,
            Result<bool>.Failure(new InvalidOperationException("Transient failure")),
            Result<bool>.Failure(new InvalidOperationException("Transient failure (retry)")),
            Result<bool>.Success(true));

        var service = new FrameExportRetryService(
            new[] { sink },
            contextFactory,
            optionsMonitor,
            clock.Object,
            metrics,
            NullLogger<FrameExportRetryService>.Instance);

        await service.StartAsync(CancellationToken.None).ConfigureAwait(false);

        var frameId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2025, 10, 13, 7, 45, 0, TimeSpan.Zero);
        var metadata = new FrameExportMetadata(
            frameId,
            timestamp,
            timestamp,
            new ExposureSettings(1500, 250, false, false),
            "Rig",
            "Camera",
            "Lens",
            35.0,
            -114.0,
            false,
            null,
            false,
            4,
            6000,
            new List<string> { "FilterA", "FilterB" },
            12.5,
            27.8,
            6040.3);

        var envelope = new FrameExportEnvelope(
            frameId,
            FrameExportStage.Processed,
            metadata,
            new ReadOnlyMemory<byte>(Enumerable.Range(0, 10).Select(static i => (byte)i).ToArray()),
            "image/jpeg",
            "jpg");

        await service.ScheduleRetryAsync(
            new FrameExportRetryRequest(envelope, sink.Name, 1, "Initial failure"),
            CancellationToken.None).ConfigureAwait(false);

        await WaitForConditionAsync(() => metrics.PendingRetryCount == 1, TimeSpan.FromSeconds(2), "Pending retry gauge should reflect enqueued payload.");
        await WaitForConditionAsync(async () => await CountRetriesAsync(contextFactory) == 1, TimeSpan.FromSeconds(2), "Retry payload should persist to telemetry store.");

        await WaitForConditionAsync(
            async () =>
            {
                var entity = await GetSingleRetryEntityAsync(contextFactory).ConfigureAwait(false);
                return entity is not null && entity.AttemptCount >= 3 && entity.LastErrorMessage == "Transient failure (retry)";
            },
            TimeSpan.FromSeconds(5),
            "Retry attempt metadata should record failure details.");

        await WaitForConditionAsync(() => sink.CallCount >= 3, TimeSpan.FromSeconds(5), "Sink should be invoked until success.");
        await WaitForConditionAsync(async () => await CountRetriesAsync(contextFactory) == 0, TimeSpan.FromSeconds(5), "Successful retry should remove telemetry record.");
        await WaitForConditionAsync(() => metrics.PendingRetryCount == 0, TimeSpan.FromSeconds(2), "Pending retry gauge should clear once queue drains.");

        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await service.StopAsync(stopCts.Token).ConfigureAwait(false);
        service.Dispose();
    }

    private static async Task<int> CountRetriesAsync(IDbContextFactory<SkyMonitorTelemetryContext> factory)
    {
        await using var context = await factory.CreateDbContextAsync().ConfigureAwait(false);
        return await context.FrameExportRetries.CountAsync().ConfigureAwait(false);
    }

    private static async Task<FrameExportRetryEntity?> GetSingleRetryEntityAsync(IDbContextFactory<SkyMonitorTelemetryContext> factory)
    {
        await using var context = await factory.CreateDbContextAsync().ConfigureAwait(false);
        return await context.FrameExportRetries.AsNoTracking().SingleOrDefaultAsync().ConfigureAwait(false);
    }

    private static async Task WaitForConditionAsync(Func<bool> predicate, TimeSpan timeout, string failureMessage)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow <= deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20)).ConfigureAwait(false);
        }

        Assert.Fail(failureMessage);
    }

    private static async Task WaitForConditionAsync(Func<Task<bool>> predicate, TimeSpan timeout, string failureMessage)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow <= deadline)
        {
            if (await predicate().ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20)).ConfigureAwait(false);
        }

        Assert.Fail(failureMessage);
    }

    private sealed class TestRetrySink : IFrameExportSink
    {
        private readonly string _name;
        private readonly FrameExportStage _stage;
        private readonly Result<bool>[] _results;
        private int _callCount;

        public TestRetrySink(string name, FrameExportStage stage, params Result<bool>[] results)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Sink name must be provided.", nameof(name));
            }

            _name = name;
            _stage = stage;
            _results = results.Length == 0 ? new[] { Result<bool>.Success(true) } : results;
        }

        public string Name => _name;

        public int CallCount => Volatile.Read(ref _callCount);

        public bool SupportsStage(FrameExportStage stage) => stage == _stage;

        public ValueTask<Result<bool>> ExportAsync(FrameExportEnvelope envelope, CancellationToken cancellationToken)
        {
            var callNumber = Interlocked.Increment(ref _callCount) - 1;
            var index = Math.Min(callNumber, _results.Length - 1);
            var result = _results[index];
            return ValueTask.FromResult(result);
        }
    }
}
