using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Components.Layout;

public sealed partial class MainLayoutFooter : ComponentBase, IAsyncDisposable
{
    private static readonly TimeSpan ClockInterval = TimeSpan.FromSeconds(1);

    private CancellationTokenSource? _clockCancellation;
    private Task? _clockTask;
    private string _localTimeDisplay = "Loading observatory time…";
    private string _localTimeTooltip = "Resolving observatory time zone…";

    [Inject]
    public IObservatoryClock ObservatoryClock { get; set; } = default!;

    [Inject]
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    [Inject]
    public ILogger<MainLayoutFooter>? Logger { get; set; }

    private string LocalTimeDisplay => _localTimeDisplay;
    private string LocalTimeTooltip => _localTimeTooltip;

    protected override void OnInitialized()
    {
        base.OnInitialized();

        ObservatoryClock.TimeZoneChanged += OnObservatoryTimeZoneChanged;
        UpdateLocalTime();

        StartClock();
    }

    private void OnObservatoryTimeZoneChanged(object? sender, EventArgs e)
    {
        _ = InvokeAsync(() =>
        {
            Logger?.LogDebug("Observatory time zone updated to {TimeZoneId} ({DisplayName})", ObservatoryClock.TimeZone.Id, ObservatoryClock.TimeZoneDisplayName);
            UpdateLocalTime();
            StateHasChanged();
        });
    }

    private void StartClock()
    {
        _clockCancellation?.Cancel();
        _clockCancellation?.Dispose();

        _clockCancellation = new CancellationTokenSource();
        _clockTask = RunClockAsync(_clockCancellation.Token);
    }

    private async Task RunClockAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                UpdateLocalTime();
                await InvokeAsync(StateHasChanged).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                Logger?.LogError(ex, "Failed to update observatory local time.");
            }

            try
            {
                await Task.Delay(ClockInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private void UpdateLocalTime()
    {
        var localTime = ObservatoryClock.LocalNow;
        var zoneLabel = ObservatoryClock.GetZoneLabel(localTime);
        _localTimeDisplay = $"{localTime:HH:mm:ss} {zoneLabel}";
        _localTimeTooltip = $"Observatory time zone: {ObservatoryClock.TimeZoneDisplayName}";
    }

    public async ValueTask DisposeAsync()
    {
        ObservatoryClock.TimeZoneChanged -= OnObservatoryTimeZoneChanged;

        if (_clockCancellation is not null)
        {
            try
            {
                _clockCancellation.Cancel();
                if (_clockTask is not null)
                {
                    await Task.WhenAny(_clockTask, Task.Delay(TimeSpan.FromMilliseconds(250)));
                }
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                _clockCancellation.Dispose();
            }
        }
    }
}
