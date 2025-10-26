using System;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.Data.Abstractions;
using HVO.SkyMonitorV5.Data.Archive;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.HostedServices;

/// <summary>
/// Ensures the image frame archive database exists and migrations are applied during application startup.
/// </summary>
public sealed class ImageFrameArchiveBootstrapper : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISkyMonitorDataPathProvider _pathProvider;
    private readonly IDataStoreBootstrapStatus _status;
    private readonly IObservatoryClock _clock;
    private readonly ILogger<ImageFrameArchiveBootstrapper> _logger;

    public ImageFrameArchiveBootstrapper(
        IServiceScopeFactory scopeFactory,
        ISkyMonitorDataPathProvider pathProvider,
        IDataStoreBootstrapStatus status,
        IObservatoryClock clock,
        ILogger<ImageFrameArchiveBootstrapper> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        _status = status ?? throw new ArgumentNullException(nameof(status));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var databasePath = _pathProvider.ResolvePath("telemetry/image_frame_archive.sqlite");
        var startedAtUtc = _clock.UtcNow;
        _logger.LogInformation("Ensuring image frame archive is present at {DatabasePath}.", databasePath);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var context = provider.GetRequiredService<ImageFrameArchiveContext>();
        try
        {
            await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

            // Enable WAL mode and set busy timeout for better concurrent access
            await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
            await context.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=5000;", cancellationToken).ConfigureAwait(false);

            var completedAtUtc = _clock.UtcNow;
            _status.ReportImageArchiveSuccess(databasePath, startedAtUtc, completedAtUtc);
            _logger.LogInformation("Image frame archive migrations completed successfully (WAL mode enabled).");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _status.ReportImageArchiveFailure(databasePath, startedAtUtc, ex);
            _logger.LogError(ex, "Failed to migrate image frame archive at {DatabasePath}.", databasePath);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
