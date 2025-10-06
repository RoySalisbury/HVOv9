#nullable enable

using System.Globalization;
using HVO.SkyMonitorV4.RPi.HostedServices.AllSkyCamera;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.SkyMonitorV4.RPi.HostedServices.AllSkyImageSave;

public sealed class AllSkyImageSaveService : BackgroundService
{
    private readonly ILogger<AllSkyImageSaveService> _logger;
    private readonly IAllSkyCameraService _cameraService;
    private readonly AllSkyImageSaveOptions _options;

    private long _lastSavedImageTicks;

    public AllSkyImageSaveService(
        ILogger<AllSkyImageSaveService> logger,
        IOptions<AllSkyImageSaveOptions> options,
        IAllSkyCameraService cameraService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cameraService = cameraService ?? throw new ArgumentNullException(nameof(cameraService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var serviceName = nameof(AllSkyImageSaveService);
        _logger.LogInformation("{BackgroundServiceName} is starting. Destination root: {Root}", serviceName, _options.ImageSaveRoot);
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
                    _logger.LogError(ex, "AllSky image save loop faulted. Restarting in {Delay}s.", _options.RestartOnFailureWaitTimeSeconds);
                    await Task.Delay(TimeSpan.FromSeconds(_options.RestartOnFailureWaitTimeSeconds), stoppingToken).ConfigureAwait(false);
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
        Directory.CreateDirectory(_options.ImageSaveRoot);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            TryPersistLatestImage();
        }
    }

    private void TryPersistLatestImage()
    {
        if (!_cameraService.IsRecording)
        {
            return;
        }

        var captureTimestamp = _cameraService.LastImageTakenTimestamp;
        if (captureTimestamp == DateTimeOffset.MinValue)
        {
            return;
        }

        var timestampTicks = captureTimestamp.UtcTicks;
        if (timestampTicks <= _lastSavedImageTicks)
        {
            return;
        }

        var maxAge = TimeSpan.FromSeconds(_options.MaxImageAgeSeconds);
        if (DateTimeOffset.UtcNow - captureTimestamp > maxAge)
        {
            return;
        }

        var fileName = _cameraService.LastImageRelativePath;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        var sourcePath = Path.Combine(_cameraService.ImageCacheRoot, fileName);
        if (!File.Exists(sourcePath))
        {
            return;
        }

        var localTimestamp = captureTimestamp.ToLocalTime();
        var destinationDirectory = Path.Combine(
            _options.ImageSaveRoot,
            localTimestamp.ToString("yyyy", CultureInfo.InvariantCulture),
            localTimestamp.ToString("MM", CultureInfo.InvariantCulture),
            localTimestamp.ToString("dd", CultureInfo.InvariantCulture),
            localTimestamp.ToString("HH", CultureInfo.InvariantCulture),
            localTimestamp.ToString("mm", CultureInfo.InvariantCulture));

        var destinationPath = Path.Combine(destinationDirectory, $"{localTimestamp.Ticks}.jpg");

        try
        {
            Directory.CreateDirectory(destinationDirectory);
            File.Copy(sourcePath, destinationPath, overwrite: false);
            _lastSavedImageTicks = timestampTicks;
            _logger.LogDebug("Persisted AllSky frame to {Path}", destinationPath);
        }
        catch (IOException ex) when (File.Exists(destinationPath))
        {
            _lastSavedImageTicks = timestampTicks;
            _logger.LogTrace(ex, "Destination file already exists for {Path}.", destinationPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist AllSky frame to {Path}", destinationPath);
        }
    }
}
