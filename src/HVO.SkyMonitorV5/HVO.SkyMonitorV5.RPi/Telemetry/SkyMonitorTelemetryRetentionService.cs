using System;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.SkyMonitorV5.RPi.Telemetry;

internal sealed class SkyMonitorTelemetryRetentionService : BackgroundService
{
    private readonly SkyMonitorTelemetryRetentionProcessor _processor;
    private readonly IOptionsMonitor<SkyMonitorTelemetryRetentionOptions> _optionsMonitor;
    private readonly ILogger<SkyMonitorTelemetryRetentionService> _logger;

    public SkyMonitorTelemetryRetentionService(
        SkyMonitorTelemetryRetentionProcessor processor,
        IOptionsMonitor<SkyMonitorTelemetryRetentionOptions> optionsMonitor,
        ILogger<SkyMonitorTelemetryRetentionService> logger)
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _optionsMonitor.CurrentValue ?? new SkyMonitorTelemetryRetentionOptions();

            try
            {
                await _processor.RunAsync(options, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (_logger.TryLogOperationCanceled(ex, stoppingToken, "Telemetry retention sweep cancelled."))
                {
                    break;
                }

                _logger.LogError(ex, "Telemetry retention sweep failed.");
            }

            var delay = options.SweepInterval;
            if (delay <= TimeSpan.Zero)
            {
                delay = TimeSpan.FromMinutes(5);
            }

            var delayCompleted = await CancellationTokenHelpers.DelayWithoutThrowAsync(delay, stoppingToken).ConfigureAwait(false);
            if (!delayCompleted)
            {
                break;
            }
        }
    }
}
