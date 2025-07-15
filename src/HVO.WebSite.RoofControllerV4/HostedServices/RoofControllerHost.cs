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
        using var _ = stoppingToken.Register(() => 
            _logger.LogDebug($"{nameof(RoofControllerHost)} background task is stopping."));

        _logger.LogDebug($"{nameof(RoofControllerHost)} background task is starting.");
        
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var initResult = await _roofController.Initialize(stoppingToken);
                    if (!initResult.IsSuccessful)
                    {
                        _logger.LogError("Failed to initialize roof controller: {Error}", initResult.Error?.Message ?? "Unknown error");
                        continue;
                    }
                    
                    // FIXED: Use ConfigureAwait(false) for better performance
                    // FIXED: Reduce logging frequency to prevent log spam
                    var logInterval = TimeSpan.FromMinutes(5);
                    var nextLogTime = DateTime.UtcNow.Add(logInterval);
                    
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        if (DateTime.UtcNow >= nextLogTime)
                        {
                            _logger.LogInformation($"{nameof(RoofControllerHost)}: Running...");
                            nextLogTime = DateTime.UtcNow.Add(logInterval);
                        }
                        
                        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken)
                            .ConfigureAwait(false);
                    }
                }
                catch (TaskCanceledException)
                {
                    _logger.LogDebug($"{nameof(RoofControllerHost)} TaskCanceledException.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{ServiceName} Error. Restarting in {RestartDelay} seconds", 
                        nameof(RoofControllerHost), _options.RestartOnFailureWaitTime);

                    await Task.Delay(TimeSpan.FromSeconds(_options.RestartOnFailureWaitTime), stoppingToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    // FIXED: Use async disposal for better performance
                    if (_roofController is IAsyncDisposable asyncDisposable)
                    {
                        await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        ((IDisposable)_roofController).Dispose();
                    }
                    _logger.LogDebug("RoofController instance disposed.");
                }
            }
        }
        finally
        {
            _logger.LogDebug($"{nameof(RoofControllerHost)} background task has stopped.");
        }
    }
}
