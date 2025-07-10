using System;
using HVO.WebSite.RoofControllerV4.Logic;
using Microsoft.Extensions.Options;

namespace HVO.WebSite.RoofControllerV4.HostedServices;

public class RoofControllerHost : BackgroundService
{
    private readonly ILogger<RoofControllerHost> _logger;
    private readonly IRoofController _roofController;
    private readonly RoofControllerHostOptions _options;

    public RoofControllerHost(ILogger<RoofControllerHost> logger, IOptions<RoofControllerHostOptions> options, IRoofController roofController)
    {
        _logger = logger;
        _roofController = roofController;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        stoppingToken.Register(() => this._logger.LogDebug($"{nameof(RoofControllerHost)} background task is stopping."));

        this._logger.LogDebug($"{nameof(RoofControllerHost)} background task is starting.");
        try
        {
            // Loop this until the service is requested to stop
            while (stoppingToken.IsCancellationRequested == false)
            {
                try
                {
                    await this._roofController.Initialize(stoppingToken);
                    try
                    {
                        // Infinite delay to keep the service running
                        while (!stoppingToken.IsCancellationRequested)
                        {
                            this._logger.LogInformation($"{nameof(RoofControllerHost)}: Running...");
                            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); // Adjust the delay as needed
                        }
                    }
                    finally
                    {
                        // We ALWAYS want to error on the side of caution and STOP the motors.  This will call dispose, which will in turn call shutdown.
                        ((IDisposable)this._roofController).Dispose();
                        this._logger.LogDebug("RoofController instance disposed.");
                    }
                }
                catch (TaskCanceledException)
                {
                    this._logger.LogDebug($"{nameof(RoofControllerHost)} TaskCanceledException.");
                    break;
                }
                catch (Exception ex)
                {
                    this._logger.LogError($"{nameof(RoofControllerHost)} Error: {ex.Message}. Restarting in {this._options.RestartOnFailureWaitTime} seconds unless cancelled.");
                    this._logger.LogError($"{nameof(RoofControllerHost)} Error: {ex.StackTrace}");

                    await Task.Delay(TimeSpan.FromSeconds(this._options.RestartOnFailureWaitTime), stoppingToken);
                }
            }
        }
        finally
        {
            this._logger.LogDebug($"{nameof(RoofControllerHost)} background task has stopped.");
        }
    }
}
