#nullable enable

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.SkyMonitorV4.RPi.HostedServices.AllSkyCamera;

public sealed class AllSkyCameraServiceHost : BackgroundService
{
    private readonly IAllSkyCameraService _cameraService;
    private readonly ILogger<AllSkyCameraServiceHost> _logger;
    private readonly AllSkyCameraServiceOptions _options;

    public AllSkyCameraServiceHost(
        IAllSkyCameraService cameraService,
        IOptions<AllSkyCameraServiceOptions> options,
        ILogger<AllSkyCameraServiceHost> logger)
    {
        _cameraService = cameraService ?? throw new ArgumentNullException(nameof(cameraService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var serviceName = nameof(AllSkyCameraServiceHost);
        _logger.LogInformation("{BackgroundServiceName} is starting.", serviceName);
        using var registration = stoppingToken.Register(() => _logger.LogInformation("{BackgroundServiceName} is stopping.", serviceName));

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _cameraService.RunAsync(stoppingToken).ConfigureAwait(false);
                    break;
                }
                catch (OperationCanceledException)
                {
                    _logger.LogDebug("{BackgroundServiceName} cancellation requested.", serviceName);
                    break;
                }
                catch (Exception ex)
                {
                    var delay = TimeSpan.FromSeconds(_options.RestartOnFailureWaitTimeSeconds);
                    _logger.LogError(ex, "{BackgroundServiceName} encountered an error. Restarting in {RestartDelay} seconds.", serviceName, _options.RestartOnFailureWaitTimeSeconds);
                    await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _logger.LogInformation("{BackgroundServiceName} has stopped.", serviceName);
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _cameraService.ClearLastImage();
        return base.StopAsync(cancellationToken);
    }
}
