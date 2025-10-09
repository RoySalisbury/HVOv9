using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Pipeline;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.SkyMonitorV5.RPi.HostedServices;

/// <summary>
/// Coordinates the background stacking pipeline. Phase 1 scaffolding creates the queueing infrastructure;
/// actual stacking logic will be layered on in subsequent phases.
/// </summary>
public sealed class BackgroundFrameStackerService : BackgroundService, IBackgroundFrameStacker, IDisposable
{
    private static readonly TimeSpan DisabledPollInterval = TimeSpan.FromSeconds(1);

    private readonly ILogger<BackgroundFrameStackerService> _logger;
    private readonly IOptionsMonitor<CameraPipelineOptions> _optionsMonitor;
    private IDisposable? _optionsReloadSubscription;

    private Channel<StackingWorkItem> _channel = null!;
    private BackgroundStackerOptions _currentOptions = new();

    public BackgroundFrameStackerService(
        IOptionsMonitor<CameraPipelineOptions> optionsMonitor,
        ILogger<BackgroundFrameStackerService> logger)
    {
        _optionsMonitor = optionsMonitor;
        _logger = logger;

        _currentOptions = optionsMonitor.CurrentValue.BackgroundStacker ?? new BackgroundStackerOptions();
        _channel = CreateChannel(_currentOptions);
        _optionsReloadSubscription = optionsMonitor.OnChange(OnOptionsChanged);
    }

    public bool IsEnabled => _currentOptions.Enabled;

    public override void Dispose()
    {
        base.Dispose();
        _channel.Writer.TryComplete();
    _optionsReloadSubscription?.Dispose();
    }

    public ValueTask<bool> EnqueueAsync(StackingWorkItem workItem, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return ValueTask.FromResult(false);
        }

        return EnqueueInternalAsync(workItem, cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Background frame stacker service starting (enabled={Enabled}).", IsEnabled);
        using var _ = stoppingToken.Register(() => _logger.LogInformation("Background frame stacker service stopping..."));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunWorkerAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ChannelClosedException ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Background stacker channel closed unexpectedly. Restarting loop after {Delay}s.", GetRestartDelay().TotalSeconds);
                await Task.Delay(GetRestartDelay(), stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background frame stacker encountered an error. Restarting after {Delay}s.", GetRestartDelay().TotalSeconds);
                await Task.Delay(GetRestartDelay(), stoppingToken).ConfigureAwait(false);
            }
        }

        _logger.LogInformation("Background frame stacker service stopped.");
    }

    private ValueTask<bool> EnqueueInternalAsync(StackingWorkItem workItem, CancellationToken cancellationToken) => EnqueueWithRetryAsync(workItem, cancellationToken);

    private async ValueTask<bool> EnqueueWithRetryAsync(StackingWorkItem workItem, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var channel = Volatile.Read(ref _channel);

            if (await channel.Writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false))
            {
                if (channel.Writer.TryWrite(workItem))
                {
                    return true;
                }

                // Channel was swapped while waiting; retry with the new instance.
                continue;
            }

            break;
        }

        return false;
    }

    private static Channel<StackingWorkItem> CreateChannel(BackgroundStackerOptions options)
    {
        var capacity = Math.Max(1, options.QueueCapacity);
        var fullMode = options.OverflowPolicy == BackgroundStackerOverflowPolicy.DropOldest
            ? BoundedChannelFullMode.DropOldest
            : BoundedChannelFullMode.Wait;

        var boundedOptions = new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
            FullMode = fullMode
        };

        return Channel.CreateBounded<StackingWorkItem>(boundedOptions);
    }

    private void OnOptionsChanged(CameraPipelineOptions options)
    {
        var newOptions = options.BackgroundStacker ?? new BackgroundStackerOptions();
        var previousOptions = _currentOptions;
        _currentOptions = newOptions;

        if (previousOptions.Enabled != newOptions.Enabled || previousOptions.QueueCapacity != newOptions.QueueCapacity || previousOptions.OverflowPolicy != newOptions.OverflowPolicy)
        {
            _logger.LogInformation(
                "Background stacker updated: Enabled={Enabled}, QueueCapacity={Capacity}, OverflowPolicy={Policy}, CompressionMode={CompressionMode}.",
                newOptions.Enabled,
                newOptions.QueueCapacity,
                newOptions.OverflowPolicy,
                newOptions.CompressionMode);

            var newChannel = CreateChannel(newOptions);
            var oldChannel = Interlocked.Exchange(ref _channel, newChannel);
            oldChannel.Writer.TryComplete();
        }
    }

    private async Task RunWorkerAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!IsEnabled)
            {
                await Task.Delay(DisabledPollInterval, stoppingToken).ConfigureAwait(false);
                continue;
            }

            var channel = Volatile.Read(ref _channel);

            StackingWorkItem workItem;
            try
            {
                workItem = await channel.Reader.ReadAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (ChannelClosedException)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                _logger.LogDebug("Background stacker channel swapped or closed; restarting read loop.");
                continue;
            }

            _logger.LogDebug(
                "Received stacking work item #{FrameNumber} at {EnqueuedAt:O}. Detailed processing will be enabled in phase 2.",
                workItem.FrameNumber,
                workItem.EnqueuedAt);

            // Phase 2 will handle forwarding to the stacking pipeline.
        }
    }

    private TimeSpan GetRestartDelay()
    {
        var seconds = Math.Clamp(_currentOptions.RestartDelaySeconds, 1, 600);
        return TimeSpan.FromSeconds(seconds);
    }
}
