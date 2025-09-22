using System;
using HVO.CLI.RoofController.Logic;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.CLI.RoofController.HostedService;

public class RoofControllerServiceV2Host : BackgroundService
{
    private readonly ILogger<RoofControllerServiceV2Host> _logger;
    private readonly IRoofControllerServiceV2 _roofControllerServiceV2;
    private readonly RoofControllerHostOptionsV2 _options;


    public RoofControllerServiceV2Host(ILogger<RoofControllerServiceV2Host> logger, IOptions<RoofControllerHostOptionsV2> options, IRoofControllerServiceV2 roofControllerServiceV2)
    {
        _logger = logger;
        _options = options.Value;
        _roofControllerServiceV2 = roofControllerServiceV2;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("{backgroundServiceName} is starting.", nameof(RoofControllerServiceV2Host));
        stoppingToken.Register(() => _logger.LogInformation("{nameof(RoofControllerServiceV2Host)} is stopping.", nameof(RoofControllerServiceV2Host)));

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var initResult = await this._roofControllerServiceV2.Initialize(stoppingToken);
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

                            _logger.LogInformation("{backgroundServiceName} is performing its work at: {time}", nameof(RoofControllerServiceV2Host), DateTimeOffset.Now);
                            nextLogTime = DateTime.UtcNow.Add(logInterval);
                        }

                        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(false);
                    }
                }
                catch (TaskCanceledException)
                {
                    _logger.LogDebug("{backgroundServiceName} TaskCanceledException.", nameof(RoofControllerServiceV2Host));
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{backgroundServiceName}  Error. Restarting in {RestartDelay} seconds.", nameof(RoofControllerServiceV2Host), _options.RestartOnFailureWaitTime);
                    await Task.Delay(TimeSpan.FromSeconds(_options.RestartOnFailureWaitTime), stoppingToken).ConfigureAwait(false);
                }
                finally
                {
                    // FIXED: Use async disposal for better performance
                    if (this._roofControllerServiceV2 is IAsyncDisposable asyncDisposable)
                    {
                        await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        ((IDisposable)this._roofControllerServiceV2).Dispose();
                    }
                    _logger.LogDebug("{backgroundServiceName} instance disposed.", nameof(RoofControllerServiceV2Host));
                }
            }
        }
        finally
        {
            _logger.LogInformation("{backgroundServiceName} has stopped.", nameof(RoofControllerServiceV2Host));
        }
    }
}

