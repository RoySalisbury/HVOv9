using System;
using System.IO;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.Data.Telemetry;
using HVO.SkyMonitorV5.Data.Telemetry.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HVO.SkyMonitorV5.RPi.Tests.Telemetry;

internal sealed class TelemetryTestHarness : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly string _workingDirectory;

    private TelemetryTestHarness(ServiceProvider provider, string workingDirectory)
    {
        _provider = provider;
        _workingDirectory = workingDirectory;
    }

    public static async Task<TelemetryTestHarness> CreateAsync()
    {
        var workingDirectory = Path.Combine(Path.GetTempPath(), $"hvo-telemetry-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workingDirectory);
        var databasePath = Path.Combine(workingDirectory, "sm-telemetry.db");

    var services = new ServiceCollection();
    services.AddLogging();
        services.AddDbContextFactory<SkyMonitorTelemetryContext>(options =>
        {
            options.UseSqlite($"Data Source={databasePath}");
        });
        services.AddDbContext<SkyMonitorTelemetryContext>(options =>
        {
            options.UseSqlite($"Data Source={databasePath}");
        });
        services.AddScoped<ISkyMonitorTelemetryRepository, SkyMonitorTelemetryRepository>();

        var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SkyMonitorTelemetryContext>();
        await context.Database.MigrateAsync().ConfigureAwait(false);

        return new TelemetryTestHarness(provider, workingDirectory);
    }

    public ISkyMonitorTelemetryRepository GetRepository()
        => _provider.GetRequiredService<ISkyMonitorTelemetryRepository>();

    public IDbContextFactory<SkyMonitorTelemetryContext> ContextFactory
        => _provider.GetRequiredService<IDbContextFactory<SkyMonitorTelemetryContext>>();

    public async Task<SkyMonitorTelemetryContext> CreateContextAsync()
        => await ContextFactory.CreateDbContextAsync().ConfigureAwait(false);

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync().ConfigureAwait(false);

        try
        {
            if (Directory.Exists(_workingDirectory))
            {
                Directory.Delete(_workingDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup failures during test teardown.
        }
    }
}
