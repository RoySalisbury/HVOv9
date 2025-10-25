using System;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.Data.Telemetry.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HVO.SkyMonitorV5.RPi.Tests.Telemetry;

[TestClass]
public sealed class SkyMonitorTelemetryRepositoryTests
{
    [TestMethod]
    public async Task SaveRemoteDispatchAttemptAsync_PersistsEntity()
    {
        await using var harness = await TelemetryTestHarness.CreateAsync().ConfigureAwait(false);
        var repository = harness.GetRepository();

        var now = DateTimeOffset.UtcNow;
        var entity = new RemoteDispatchAttemptEntity
        {
            AttemptedAtUtc = now,
            AttemptedAtLocal = now,
            Mode = "Test",
            Outcome = 1,
            LatencyMilliseconds = 123.45,
            PayloadBytes = 2048,
            PayloadContentType = "image/png",
            PayloadExtension = ".png",
            Message = "Success",
            ErrorMessage = null,
            FormatKey = "png",
        };

        await repository.SaveRemoteDispatchAttemptAsync(entity).ConfigureAwait(false);

        await using var context = await harness.CreateContextAsync().ConfigureAwait(false);
        var persisted = await context.RemoteDispatchAttempts.SingleAsync().ConfigureAwait(false);

        Assert.AreEqual(entity.Mode, persisted.Mode, "Mode should round-trip through the repository.");
        Assert.AreEqual(entity.FormatKey, persisted.FormatKey, "Format key should persist.");
        Assert.AreEqual(entity.PayloadBytes, persisted.PayloadBytes, "Payload bytes should persist.");
    }

    [TestMethod]
    public async Task SaveTelemetryEventAsync_PersistsStructuredLog()
    {
        await using var harness = await TelemetryTestHarness.CreateAsync().ConfigureAwait(false);
        var repository = harness.GetRepository();

        var occurredAt = DateTimeOffset.UtcNow;
        var entity = new TelemetryEventEntity
        {
            OccurredAtUtc = occurredAt,
            OccurredAtLocal = occurredAt,
            Category = "RemoteDispatch",
            EventType = "RemoteDispatchSucceeded",
            Severity = "Information",
            Summary = "Dispatched frame",
            Detail = null,
            PropertiesJson = "{\"mode\":\"Test\"}"
        };

        await repository.SaveTelemetryEventAsync(entity).ConfigureAwait(false);

        await using var context = await harness.CreateContextAsync().ConfigureAwait(false);
        var persisted = await context.TelemetryEvents.SingleAsync().ConfigureAwait(false);

        Assert.AreEqual(entity.EventType, persisted.EventType, "Event type should persist.");
        Assert.AreEqual(entity.Severity, persisted.Severity, "Severity should persist.");
        Assert.AreEqual(entity.PropertiesJson, persisted.PropertiesJson, "Properties JSON should persist verbatim.");
    }
}
