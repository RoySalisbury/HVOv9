using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics.Metrics;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using Moq;

namespace HVO.SkyMonitorV5.RPi.Tests.Telemetry;

[TestClass]
public sealed class SkyMonitorTelemetryIngestionServiceTests
{
    [TestMethod]
    public async Task ExecuteAsync_PersistsEnqueuedTelemetryWorkItems()
    {
        await using var harness = await TelemetryTestHarness.CreateAsync().ConfigureAwait(false);

        var queue = new SkyMonitorTelemetryIngestionQueue();
        using var meter = new Meter("HVO.SkyMonitor.Telemetry.Tests");
        var meterFactory = new TestMeterFactory(meter);
        var metricsLogger = NullLogger<SkyMonitorTelemetryMetrics>.Instance;
        using var metrics = new SkyMonitorTelemetryMetrics(meterFactory, queue, metricsLogger);

        var clock = new Mock<IObservatoryClock>();
        clock.SetupGet(c => c.UtcNow).Returns(() => DateTimeOffset.UtcNow);

        var service = new SkyMonitorTelemetryIngestionService(
            queue,
            harness.ScopeFactory,
            clock.Object,
            metrics,
            NullLogger<SkyMonitorTelemetryIngestionService>.Instance);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token).ConfigureAwait(false);

        var nowUtc = DateTimeOffset.UtcNow;
        var localNow = nowUtc;

        queue.TryWrite(new TelemetryWorkItem.RemoteDispatchAttempt(
            nowUtc,
            new RemoteDispatchAttemptPayload(
            AttemptedAtUtc: nowUtc,
            AttemptedAtLocal: localNow,
            Mode: "TestMode",
            Outcome: Models.RemoteDispatchOutcome.Succeeded,
            LatencyMilliseconds: 42.5,
            PayloadBytes: 1024,
            PayloadContentType: "image/png",
            PayloadExtension: ".png",
            Message: "Success",
            ErrorMessage: null,
            FormatKey: "png")));

        queue.TryWrite(new TelemetryWorkItem.TelemetryEvent(
            nowUtc,
            new TelemetryEventPayload(
            OccurredAtUtc: nowUtc,
            OccurredAtLocal: localNow,
            Category: "Diagnostics",
            EventType: "IngestionTest",
            Severity: "Information",
            Summary: "Telemetry ingestion test",
            Detail: "Background service should persist this event.",
            PropertiesJson: "{\"test\":true}")));

        await EventuallyAsync(async () =>
        {
            await using var context = await harness.CreateContextAsync().ConfigureAwait(false);
            var remoteAttempts = await context.RemoteDispatchAttempts.CountAsync().ConfigureAwait(false);
            var telemetryEvents = await context.TelemetryEvents.CountAsync().ConfigureAwait(false);
            return remoteAttempts > 0 && telemetryEvents > 0;
        }).ConfigureAwait(false);

        cts.Cancel();
        await service.StopAsync(CancellationToken.None).ConfigureAwait(false);

        await using var verification = await harness.CreateContextAsync().ConfigureAwait(false);
        var attempt = await verification.RemoteDispatchAttempts.SingleAsync().ConfigureAwait(false);
        var telemetryEvent = await verification.TelemetryEvents.SingleAsync().ConfigureAwait(false);

        Assert.AreEqual("TestMode", attempt.Mode, "Remote dispatch attempt should retain the original mode value.");
        Assert.AreEqual("IngestionTest", telemetryEvent.EventType, "Telemetry event should retain the original event type.");
    }

    private sealed class TestMeterFactory : IMeterFactory, IDisposable
    {
        private readonly Meter _meter;

        public TestMeterFactory(Meter meter)
        {
            _meter = meter ?? throw new ArgumentNullException(nameof(meter));
        }

        public Meter Create(MeterOptions options)
        {
            return _meter;
        }

        public Meter Create(string name)
        {
            return _meter;
        }

        public void Dispose()
        {
            _meter.Dispose();
        }
    }

    private static async Task EventuallyAsync(Func<Task<bool>> condition, TimeSpan? timeout = null, TimeSpan? pollInterval = null)
    {
        timeout ??= TimeSpan.FromSeconds(5);
        pollInterval ??= TimeSpan.FromMilliseconds(50);

        var expiry = DateTime.UtcNow + timeout.Value;
        while (DateTime.UtcNow < expiry)
        {
            if (await condition().ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(pollInterval.Value).ConfigureAwait(false);
        }

        Assert.Fail($"Condition was not satisfied within the allotted timeout of {timeout}.");
    }
}
