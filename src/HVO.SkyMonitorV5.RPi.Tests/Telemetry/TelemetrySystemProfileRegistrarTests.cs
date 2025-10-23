using System;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HVO.SkyMonitorV5.RPi.Tests.Telemetry;

[TestClass]
public sealed class TelemetrySystemProfileRegistrarTests
{
    [TestMethod]
    public async Task RegisterAsync_PersistsSystemProfileMetadata()
    {
        await using var harness = await TelemetryTestHarness.CreateAsync().ConfigureAwait(false);

        var collector = new TelemetrySystemProfileCollector(NullLogger<TelemetrySystemProfileCollector>.Instance);
        var registrar = new TelemetrySystemProfileRegistrar(collector, harness.GetRepository(), NullLogger<TelemetrySystemProfileRegistrar>.Instance);

        var observedAt = DateTimeOffset.UtcNow;
        await registrar.RegisterAsync(observedAt, CancellationToken.None).ConfigureAwait(false);

        await using var context = await harness.CreateContextAsync().ConfigureAwait(false);
        var profiles = await context.TelemetrySystemProfiles.ToListAsync().ConfigureAwait(false);

        Assert.HasCount(1, profiles, "System profile should be recorded once per system hash.");
        var profile = profiles[0];
        Assert.IsFalse(string.IsNullOrWhiteSpace(profile.SystemHash), "System hash should be populated.");
        Assert.IsTrue(profile.LastSeenAtUtc >= profile.FirstSeenAtUtc, "Last seen timestamp should be greater than or equal to the first seen timestamp.");
        Assert.IsGreaterThanOrEqualTo(observedAt.AddMinutes(-1), profile.LastSeenAtUtc, "Recorded timestamp should be close to the observed timestamp.");
    }
}
