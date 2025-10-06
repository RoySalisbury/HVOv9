#nullable enable

using System.Globalization;
using System.Linq;
using System.Threading;
using HVO.SkyMonitorV4.RPi.HostedServices.AllSkyImageSave;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.SkyMonitorV4.RPi.HostedServices.AllSkyImageCleanup;

public sealed class AllSkyImageCleanupService : BackgroundService
{
    private readonly ILogger<AllSkyImageCleanupService> _logger;
    private readonly AllSkyImageCleanupOptions _cleanupOptions;
    private readonly AllSkyImageSaveOptions _saveOptions;

    public AllSkyImageCleanupService(
        ILogger<AllSkyImageCleanupService> logger,
    IOptions<AllSkyImageCleanupOptions> cleanupOptions,
    IOptions<AllSkyImageSaveOptions> saveOptions)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cleanupOptions = cleanupOptions?.Value ?? throw new ArgumentNullException(nameof(cleanupOptions));
        _saveOptions = saveOptions?.Value ?? throw new ArgumentNullException(nameof(saveOptions));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var serviceName = nameof(AllSkyImageCleanupService);
        _logger.LogInformation("{BackgroundServiceName} is starting. Monitoring {Root}.", serviceName, _saveOptions.ImageSaveRoot);
        using var registration = stoppingToken.Register(() => _logger.LogInformation("{BackgroundServiceName} is stopping.", serviceName));

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunLoopAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "AllSky cleanup loop faulted. Restarting in {Delay}s.", _cleanupOptions.RestartOnFailureWaitTimeSeconds);
                    await Task.Delay(TimeSpan.FromSeconds(_cleanupOptions.RestartOnFailureWaitTimeSeconds), stoppingToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _logger.LogInformation("{BackgroundServiceName} has stopped.", serviceName);
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.Now;
            var nextRun = CalculateNextRun(now);
            var delay = nextRun - now;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                ExecuteCleanup(nextRun);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to complete AllSky image cleanup cycle.");
            }
        }
    }

    private static DateTimeOffset CalculateNextRun(DateTimeOffset reference)
    {
        var scheduled = new DateTimeOffset(
            reference.Year,
            reference.Month,
            reference.Day,
            reference.Hour,
            30,
            30,
            reference.Offset).AddHours(1);

        if (scheduled <= reference)
        {
            scheduled = scheduled.AddHours(1);
        }

        return scheduled;
    }

    private void ExecuteCleanup(DateTimeOffset executionTime)
    {
        var root = _saveOptions.ImageSaveRoot;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            _logger.LogDebug("AllSky image directory {Root} not found. Skipping cleanup run.", root);
            return;
        }

        var cutoff = executionTime.LocalDateTime.AddHours(-_cleanupOptions.MaxHoursToKeep);
        var cutoffTicks = cutoff.Ticks;
        _logger.LogInformation("Running image cleanup for files older than {Cutoff}.", cutoff);

        var files = Directory.EnumerateFiles(root, "*.jpg", SearchOption.AllDirectories).ToArray();
        var deletedCount = 0;

        Parallel.ForEach(files, filePath =>
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            if (!long.TryParse(fileName, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks))
            {
                return;
            }

            if (ticks > cutoffTicks)
            {
                return;
            }

            try
            {
                File.Delete(filePath);
                Interlocked.Increment(ref deletedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete expired AllSky frame {Path}", filePath);
            }
        });

        if (deletedCount > 0)
        {
            _logger.LogInformation("Deleted {Count} expired AllSky frames.", deletedCount);
        }

        CleanupEmptyDirectories(root);
    }

    private void CleanupEmptyDirectories(string parentDirectory)
    {
        foreach (var directory in Directory.EnumerateDirectories(parentDirectory))
        {
            CleanupEmptyDirectories(directory);

            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory, recursive: false);
                    _logger.LogDebug("Removed empty directory {Directory}", directory);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete empty directory {Directory}", directory);
            }
        }
    }
}
