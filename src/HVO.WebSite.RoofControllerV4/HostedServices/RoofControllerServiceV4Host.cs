using System;
using HVO.WebSite.RoofControllerV4.Logic;
using HVO.WebSite.RoofControllerV4.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.WebSite.RoofControllerV4.HostedServices;

public class RoofControllerServiceV4Host : BackgroundService
{
    private readonly ILogger<RoofControllerServiceV4Host> _logger;
    private readonly IRoofControllerServiceV4 _roofControllerServiceV4;
    private readonly RoofControllerHostOptionsV4 _options;


    public RoofControllerServiceV4Host(ILogger<RoofControllerServiceV4Host> logger, IOptions<RoofControllerHostOptionsV4> options, IRoofControllerServiceV4 roofControllerServiceV4)
    {
        _logger = logger;
        _options = options.Value;
        _roofControllerServiceV4 = roofControllerServiceV4;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("{backgroundServiceName} is starting.", nameof(RoofControllerServiceV4Host));
        stoppingToken.Register(() => _logger.LogInformation("{backgroundServiceName} is stopping.", nameof(RoofControllerServiceV4Host)));

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var initResult = await this._roofControllerServiceV4.Initialize(stoppingToken);
                    if (!initResult.IsSuccessful)
                    {
                        _logger.LogError("Failed to initialize roof controller: {Error}", initResult.Error?.Message ?? "Unknown error");
                        continue;
                    }

                    var logInterval = TimeSpan.FromMinutes(5);
                    var nextLogTime = DateTime.UtcNow.Add(logInterval);

                    while (!stoppingToken.IsCancellationRequested)
                    {
                        if (DateTime.UtcNow >= nextLogTime)
                        {
                            // Can setup the watchdog here.......

                            _logger.LogInformation("{backgroundServiceName} is performing its work at: {time}", nameof(RoofControllerServiceV4Host), DateTimeOffset.Now);
                            nextLogTime = DateTime.UtcNow.Add(logInterval);
                        }

                        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(false);
                    }
                }
                catch (TaskCanceledException)
                {
                    _logger.LogDebug("{backgroundServiceName} TaskCanceledException.", nameof(RoofControllerServiceV4Host));
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{backgroundServiceName}  Error. Restarting in {RestartDelay} seconds.", nameof(RoofControllerServiceV4Host), _options.RestartOnFailureWaitTime);
                    await Task.Delay(TimeSpan.FromSeconds(_options.RestartOnFailureWaitTime), stoppingToken).ConfigureAwait(false);
                }
                finally
                {
                    // FIXED: Use async disposal for better performance
                    if (this._roofControllerServiceV4 is IAsyncDisposable asyncDisposable)
                    {
                        await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        ((IDisposable)this._roofControllerServiceV4).Dispose();
                    }
                    _logger.LogDebug("{backgroundServiceName} instance disposed.", nameof(RoofControllerServiceV4Host));
                }
            }
        }
        finally
        {
            _logger.LogInformation("{backgroundServiceName} has stopped.", nameof(RoofControllerServiceV4Host));
        }
    }
}

