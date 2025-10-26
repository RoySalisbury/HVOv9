#nullable enable

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV4.CLI.HostedServices;

public sealed class CliRunnerHostedService : BackgroundService
{
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(5);
    private readonly ILogger<CliRunnerHostedService> _logger;

    public CliRunnerHostedService(ILogger<CliRunnerHostedService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var serviceName = nameof(CliRunnerHostedService);
        using var registration = stoppingToken.Register(() =>
            _logger.LogInformation("{ServiceName} received cancellation request.", serviceName));

        _logger.LogInformation("{ServiceName} starting.", serviceName);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAsync(stoppingToken).ConfigureAwait(false);
                break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ServiceName} loop faulted. Restarting in {Delay}s.", serviceName, RestartDelay.TotalSeconds);
                await Task.Delay(RestartDelay, stoppingToken).ConfigureAwait(false);
            }
        }

        _logger.LogInformation("{ServiceName} stopped.", serviceName);
    }

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CLI processing loop started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogDebug("CLI heartbeat at {Timestamp}.", DateTimeOffset.Now);
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ConfigureAwait(false);
        }

        _logger.LogInformation("CLI processing loop exiting.");
    }
}
