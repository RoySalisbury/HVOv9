using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.Data.Abstractions;
using HVO.SkyMonitorV5.Data.Telemetry;
using HVO.SkyMonitorV5.RPi.HostedServices;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HVO.SkyMonitorV5.RPi.Tests.Telemetry;

[TestClass]
public sealed class TelemetryStoreBootstrapperTests
{
    [TestMethod]
    public async Task StartAsync_CreatesDatabaseAndAppliesMigrations()
    {
        var workingDirectory = Path.Combine(Path.GetTempPath(), $"hvo-telemetry-bootstrap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workingDirectory);
        var databasePath = Path.Combine(workingDirectory, "sm-telemetry.db");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ISkyMonitorDataPathProvider>(new StubDataPathProvider(workingDirectory));
        services.AddDbContext<SkyMonitorTelemetryContext>(options =>
        {
            options.UseSqlite($"Data Source={databasePath}", sqlite => sqlite.MigrationsAssembly(typeof(SkyMonitorTelemetryContext).Assembly.FullName));
        });

        var provider = services.BuildServiceProvider();

        var bootstrapStatus = new DataStoreBootstrapStatus();
        var clock = new StubObservatoryClock();

        var bootstrapper = new TelemetryStoreBootstrapper(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ISkyMonitorDataPathProvider>(),
            bootstrapStatus,
            clock,
            provider.GetRequiredService<ILogger<TelemetryStoreBootstrapper>>());

        await bootstrapper.StartAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.IsTrue(File.Exists(databasePath), "Telemetry database file should exist after bootstrapper runs.");

        await using var context = provider.GetRequiredService<SkyMonitorTelemetryContext>();
        var eventCount = await context.TelemetryEvents.CountAsync().ConfigureAwait(false);
        Assert.AreEqual(0, eventCount, "Telemetry event table should exist even if empty after migrations.");

        var snapshot = bootstrapStatus.GetSnapshot();
        Assert.IsTrue(snapshot.Telemetry.Ran && snapshot.Telemetry.Succeeded, "Bootstrap status should capture successful telemetry migration.");

        await bootstrapper.StopAsync(CancellationToken.None).ConfigureAwait(false);

        provider.Dispose();

        Directory.Delete(workingDirectory, recursive: true);
    }

    private sealed class StubDataPathProvider : ISkyMonitorDataPathProvider
    {
        private readonly string _root;

        public StubDataPathProvider(string root)
        {
            _root = root;
        }

        public string RootPath => _root;

        public string ResolvePath(string relativePath)
        {
            var combined = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var directory = Path.GetDirectoryName(combined);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return combined;
        }
    }

    private sealed class StubObservatoryClock : IObservatoryClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

        public DateTimeOffset LocalNow => DateTimeOffset.UtcNow;

        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;

        public string TimeZoneDisplayName => "UTC";

        public DateTimeOffset ToLocal(DateTimeOffset timestamp) => timestamp;

        public string GetZoneLabel(DateTimeOffset timestamp) => "UTC";

        public event EventHandler? TimeZoneChanged
        {
            add { }
            remove { }
        }
    }
}
