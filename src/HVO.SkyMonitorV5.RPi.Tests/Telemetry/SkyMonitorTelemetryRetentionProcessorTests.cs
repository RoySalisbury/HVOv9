using System;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.Data.Telemetry.Entities;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HVO.SkyMonitorV5.RPi.Tests.Telemetry;

[TestClass]
public sealed class SkyMonitorTelemetryRetentionProcessorTests
{
    [TestMethod]
    public async Task RunAsync_RemovesRecordsExceedingAgePolicy()
    {
        await using var harness = await TelemetryTestHarness.CreateAsync().ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;

        await using (var context = await harness.CreateContextAsync().ConfigureAwait(false))
        {
            context.RemoteDispatchAttempts.Add(new RemoteDispatchAttemptEntity
            {
                AttemptedAtUtc = now.AddDays(-31),
                AttemptedAtLocal = now.AddDays(-31),
                Mode = "Test",
                Outcome = 2,
                Message = "Old",
            });

            context.RemoteDispatchAttempts.Add(new RemoteDispatchAttemptEntity
            {
                AttemptedAtUtc = now,
                AttemptedAtLocal = now,
                Mode = "Test",
                Outcome = 1,
                Message = "Recent",
            });

            await context.SaveChangesAsync().ConfigureAwait(false);
        }

        var clock = new TestClock(now);
        var processor = new SkyMonitorTelemetryRetentionProcessor(harness.ContextFactory, clock, NullLogger<SkyMonitorTelemetryRetentionProcessor>.Instance);
        var options = new SkyMonitorTelemetryRetentionOptions
        {
            RemoteDispatch = TelemetryRetentionPolicy.Create(TimeSpan.FromDays(30), null),
            BackgroundStacker = TelemetryRetentionPolicy.Create(null, null),
            CapturePacing = TelemetryRetentionPolicy.Create(null, null),
            ProcessingQueue = TelemetryRetentionPolicy.Create(null, null),
            FilterMetrics = TelemetryRetentionPolicy.Create(null, null),
            TelemetryEvents = TelemetryRetentionPolicy.Create(null, null),
            VacuumAfterPurge = false,
            SweepInterval = TimeSpan.FromMinutes(5)
        };

        var summary = await processor.RunAsync(options, CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(1, summary.RemoteDispatchPurged, "One stale remote dispatch attempt should have been purged.");
        Assert.AreEqual(1, summary.TotalPurged, "Only the stale record should have been removed.");

        await using var verification = await harness.CreateContextAsync().ConfigureAwait(false);
        var remaining = await verification.RemoteDispatchAttempts.ToListAsync().ConfigureAwait(false);
        Assert.AreEqual(1, remaining.Count);
        Assert.AreEqual("Recent", remaining[0].Message);
    }

    private sealed class TestClock : IObservatoryClock
    {
        public TestClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; private set; }

        public DateTimeOffset LocalNow => TimeZoneInfo.ConvertTime(UtcNow, TimeZone);

        public TimeZoneInfo TimeZone { get; private set; } = TimeZoneInfo.Utc;

        public string TimeZoneDisplayName => TimeZone.DisplayName;

        public event EventHandler? TimeZoneChanged;

        public string GetZoneLabel(DateTimeOffset localTime) => TimeZone.Id;

        public DateTimeOffset ToLocal(DateTimeOffset timestamp) => TimeZoneInfo.ConvertTime(timestamp, TimeZone);

        public void Advance(TimeSpan interval)
        {
            UtcNow = UtcNow.Add(interval);
            TimeZoneChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
