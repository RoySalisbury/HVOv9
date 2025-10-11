using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Components.Pages;

public sealed partial class Diagnostics : ComponentBase, IAsyncDisposable
{
    private const int HistoryCapacity = 60;
    private static readonly TimeSpan SystemRefreshInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan QueueRefreshInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FilterRefreshInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan BackgroundRefreshInterval = TimeSpan.FromSeconds(3);

    private readonly List<double> _queueFillHistory = new();
    private readonly List<double> _queueLatencyHistory = new();
    private readonly List<double> _stackDurationHistory = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private CancellationTokenSource? _refreshCts;
    private Task? _refreshTask;
    private BackgroundStackerMetricsResponse? _stackerMetrics;
    private FilterMetricsSnapshot? _filterMetrics;
    private SystemDiagnosticsSnapshot? _systemDiagnostics;
    private DateTimeOffset? _lastUpdated;
    private string? _errorMessage;
    private bool _isLoading = true;
    private DiagnosticsTab _activeTab = DiagnosticsTab.System;
    private DateTimeOffset _lastSystemRefreshUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastQueueRefreshUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastFilterRefreshUtc = DateTimeOffset.MinValue;

    [Inject]
    private IDiagnosticsService DiagnosticsService { get; set; } = default!;

    [Inject]
    private ILogger<Diagnostics> Logger { get; set; } = default!;

    [Inject]
    private IObservatoryClock ObservatoryClock { get; set; } = default!;

    private bool IsLoading => _isLoading;
    private BackgroundStackerMetricsResponse? StackerMetrics => _stackerMetrics;
    private FilterMetricsSnapshot? FilterMetricsSnapshot => _filterMetrics;
    private string? ErrorMessage => _errorMessage;
    private string? LastUpdatedDisplay => _lastUpdated?.ToString("HH:mm:ss", CultureInfo.CurrentCulture);
    private string RefreshIntervalDisplay => string.Create(CultureInfo.CurrentCulture, $"{GetCurrentLoopInterval().TotalSeconds:F1} s");
    private string AutoRefreshStatus => _refreshCts is { IsCancellationRequested: false } ? $"{ActiveTab} metrics" : "Paused";
    private string DiagnosticsHealthDisplay => string.IsNullOrEmpty(_errorMessage) ? "Nominal" : "Needs attention";
    private DiagnosticsTab ActiveTab => _activeTab;
    private string QueueFillGaugeStyle => BuildGaugeStyle(_stackerMetrics?.QueueFillPercentage ?? 0);
    private string QueueFillPercentageDisplay => _stackerMetrics is { } metrics ? FormatPercent(metrics.QueueFillPercentage) : "—";
    private string QueueDepthSummary => _stackerMetrics is { } metrics ? FormatDepth(metrics.QueueDepth, metrics.QueueCapacity) : "—";
    private string PeakQueueDepthSummary => _stackerMetrics is { } metrics ? FormatCount(metrics.PeakQueueDepth) : "—";
    private string PeakQueueFillDisplay => _stackerMetrics is { } metrics ? FormatPercent(metrics.PeakQueueFillPercentage) : "—";
    private string QueuePressureDisplay => _stackerMetrics is { } metrics ? DescribeQueuePressure(metrics.QueuePressureLevel) : "—";
    private string SecondsSinceLastCompletedDisplay => _stackerMetrics switch
    {
        { SecondsSinceLastCompleted: { } seconds } => FormatSeconds((double?)seconds),
        { ProcessedFrameCount: > 0 } => FormatSeconds(0d),
        _ => "No frames yet"
    };
    private string ProcessedFrameCountDisplay => _stackerMetrics is { } metrics ? FormatCount(metrics.ProcessedFrameCount) : "—";
    private string DroppedFrameCountDisplay => _stackerMetrics is { } metrics ? FormatCount(metrics.DroppedFrameCount) : "—";
    private string QueueMemorySummary => _stackerMetrics is { } metrics ? FormatMemory(metrics.QueueMemoryMegabytes) : "—";
    private string PeakQueueMemorySummary => _stackerMetrics is { } metrics ? FormatMemory(metrics.PeakQueueMemoryMegabytes) : "—";
    private string LastFrameNumberDisplay => _stackerMetrics?.LastFrameNumber?.ToString("N0", CultureInfo.CurrentCulture) ?? "—";
    private string HistoryDurationDisplay => BuildHistoryDurationLabel(_queueFillHistory.Count);
    private string LatencyMaxDisplay => BuildMaxLabel(_queueLatencyHistory, "ms");
    private string StackDurationMaxDisplay => BuildMaxLabel(_stackDurationHistory, "ms");
    private string FilterSummaryDisplay => _filterMetrics is { Filters.Count: > 0 }
        ? $"{_filterMetrics.Filters.Count} active filters"
        : "No filter telemetry yet";

    private SystemDiagnosticsSnapshot? SystemDiagnostics => _systemDiagnostics;
    private IReadOnlyList<CoreCpuLoad> CoreCpuLoads => _systemDiagnostics?.CoreCpuLoads ?? Array.Empty<CoreCpuLoad>();
    private bool HasCoreCpuLoads => CoreCpuLoads.Count > 0;
    private double TotalCpuGaugeValue => _systemDiagnostics switch
    {
        { TotalCpuPercent: { } total } => total,
        { } metrics => metrics.ProcessCpuPercent,
        _ => 0d
    };
    private string TotalCpuGaugeStyle => BuildGaugeStyle(TotalCpuGaugeValue);
    private string TotalCpuDisplay => _systemDiagnostics switch
    {
        { TotalCpuPercent: { } total } => FormatPercent(total),
        { } metrics => FormatPercent(metrics.ProcessCpuPercent),
        _ => "—"
    };
    private string TotalCpuGaugeLabel => _systemDiagnostics is { TotalCpuPercent: { } } ? "system" : "process";
    private string ProcessCpuDisplay => _systemDiagnostics is { } metrics ? FormatPercent(metrics.ProcessCpuPercent) : "—";
    private string ProcessThreadsDisplay => _systemDiagnostics is { } metrics ? metrics.ThreadCount.ToString("N0", CultureInfo.CurrentCulture) : "—";
    private string ProcessUptimeDisplay => FormatDuration(_systemDiagnostics?.UptimeSeconds);
    private double MemoryGaugeValue => _systemDiagnostics is { } metrics
        ? metrics.Memory.UsagePercent ?? CalculateMemoryUsagePercent(metrics.Memory) ?? 0d
        : 0d;
    private string MemoryUsageGaugeStyle => BuildGaugeStyle(MemoryGaugeValue);
    private string MemoryUsageDisplay => _systemDiagnostics is { } metrics
        ? FormatPercent(metrics.Memory.UsagePercent ?? CalculateMemoryUsagePercent(metrics.Memory))
        : "—";
    private string SystemMemoryTotalDisplay => FormatMegabytes(_systemDiagnostics?.Memory.TotalMegabytes);
    private string SystemMemoryUsedDisplay => FormatMegabytes(_systemDiagnostics?.Memory.UsedMegabytes);
    private string SystemMemoryAvailableDisplay => FormatMegabytes(_systemDiagnostics?.Memory.AvailableMegabytes);
    private string SystemMemoryFreeDisplay => FormatMegabytes(_systemDiagnostics?.Memory.FreeMegabytes);
    private string SystemMemoryCachedDisplay => FormatMegabytes(_systemDiagnostics?.Memory.CachedMegabytes);
    private string SystemMemoryBuffersDisplay => FormatMegabytes(_systemDiagnostics?.Memory.BuffersMegabytes);
    private string ProcessWorkingSetDisplay => _systemDiagnostics is { } metrics ? FormatMegabytes(metrics.ProcessWorkingSetMegabytes) : "—";
    private string ProcessPrivateDisplay => _systemDiagnostics is { } metrics ? FormatMegabytes(metrics.ProcessPrivateMegabytes) : "—";
    private string ManagedMemoryDisplay => _systemDiagnostics is { } metrics ? FormatMegabytes(metrics.ManagedMemoryMegabytes) : "—";

    private string? GetAriaCurrent(DiagnosticsTab tab) => _activeTab == tab ? "page" : null;
    private string GetTabCss(DiagnosticsTab tab) => _activeTab == tab ? "active" : string.Empty;

    private TimeSpan GetCurrentLoopInterval() => _activeTab switch
    {
        DiagnosticsTab.System => SystemRefreshInterval,
        DiagnosticsTab.Queue => QueueRefreshInterval,
        DiagnosticsTab.Filters => FilterRefreshInterval,
        _ => BackgroundRefreshInterval
    };

    protected override async Task OnInitializedAsync()
    {
        _refreshCts = new CancellationTokenSource();
        await RefreshAsync(_refreshCts.Token);

        if (!_refreshCts.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), _refreshCts.Token);
                await RefreshAsync(_refreshCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Component disposed before initial warm-up completed.
            }
        }

        _refreshTask = RunRefreshLoopAsync(_refreshCts.Token);
    }

    private void SetActiveTab(DiagnosticsTab tab)
    {
        if (_activeTab == tab)
        {
            return;
        }

        _activeTab = tab;
        if (tab is DiagnosticsTab.System)
        {
            _ = InvokeAsync(async () =>
            {
                try
                {
                    await RefreshAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Switching tabs while shutting down refresh loop.
                }
            });
        }
        StateHasChanged();
    }

    private async Task RefreshNowAsync()
    {
        if (_refreshCts?.IsCancellationRequested == true)
        {
            return;
        }

        await RefreshAsync(CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);

        if (_refreshCts is not null)
        {
            _refreshCts.Cancel();
        }

        if (_refreshTask is not null)
        {
            try
            {
                await _refreshTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Diagnostics refresh loop ended with an error during disposal.");
            }
        }

        _refreshCts?.Dispose();

        _refreshLock.Dispose();
    }

    private async Task RunRefreshLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var delay = GetCurrentLoopInterval();
                await Task.Delay(delay, cancellationToken);
                await RefreshAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Diagnostics refresh loop encountered an unexpected error.");
            _errorMessage = "Diagnostics refresh loop encountered an unexpected error. Check logs for details.";
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken);

        try
        {
            var errorMessages = new List<string>();
            BackgroundStackerMetricsResponse? latestMetrics = null;
            var historyApplied = false;
                var nowUtc = ObservatoryClock.UtcNow;

                if (ShouldRefreshQueueMetrics() && nowUtc - _lastQueueRefreshUtc >= QueueRefreshInterval)
            {
                try
                {
                    var stackerResult = await DiagnosticsService.GetBackgroundStackerMetricsAsync(cancellationToken).ConfigureAwait(false);
                    if (stackerResult.IsSuccessful)
                    {
                        var metrics = stackerResult.Value;
                        latestMetrics = metrics;
                        _stackerMetrics = metrics;
                        _lastUpdated = ObservatoryClock.LocalNow;
                    }
                    else
                    {
                        var error = stackerResult.Error ?? new InvalidOperationException("Unknown diagnostics error");
                        Logger.LogWarning(error, "Failed to refresh background stacker metrics.");
                        errorMessages.Add("Unable to retrieve background stacker metrics.");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Unexpected error refreshing background stacker metrics.");
                    errorMessages.Add("Unexpected error while retrieving background stacker metrics.");
                }

                try
                {
                    var historyResult = await DiagnosticsService.GetBackgroundStackerHistoryAsync(cancellationToken).ConfigureAwait(false);
                    if (historyResult.IsSuccessful)
                    {
                        ApplyHistory(historyResult.Value.Samples);
                        historyApplied = true;
                    }
                    else
                    {
                        var error = historyResult.Error ?? new InvalidOperationException("Unknown diagnostics history error");
                        Logger.LogWarning(error, "Failed to refresh background stacker history samples.");
                        errorMessages.Add("Unable to retrieve background stacker history.");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Unexpected error refreshing background stacker history.");
                    errorMessages.Add("Unexpected error while retrieving background stacker history.");
                }

                if (!historyApplied && latestMetrics is not null)
                {
                    UpdateHistory(_queueFillHistory, latestMetrics.QueueFillPercentage);
                    UpdateHistory(_queueLatencyHistory, latestMetrics.LastQueueLatencyMilliseconds ?? 0d);
                    UpdateHistory(_stackDurationHistory, latestMetrics.LastStackMilliseconds ?? 0d);
                }
                    _lastQueueRefreshUtc = nowUtc;
            }

                if (ShouldRefreshFilterMetrics() && nowUtc - _lastFilterRefreshUtc >= FilterRefreshInterval)
            {
                try
                {
                    var filterResult = await DiagnosticsService.GetFilterMetricsAsync(cancellationToken).ConfigureAwait(false);
                    if (filterResult.IsSuccessful)
                    {
                        _filterMetrics = filterResult.Value;
                    }
                    else
                    {
                        var error = filterResult.Error ?? new InvalidOperationException("Unknown diagnostics error");
                        Logger.LogWarning(error, "Failed to refresh filter metrics.");
                        errorMessages.Add("Unable to retrieve filter telemetry.");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Unexpected error refreshing filter metrics.");
                    errorMessages.Add("Unexpected error while retrieving filter telemetry.");
                }
                    _lastFilterRefreshUtc = nowUtc;
            }

                if (ShouldRefreshSystemMetrics() && nowUtc - _lastSystemRefreshUtc >= SystemRefreshInterval)
            {
                try
                {
                    var systemResult = await DiagnosticsService.GetSystemDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
                    if (systemResult.IsSuccessful)
                    {
                        _systemDiagnostics = systemResult.Value;
                    }
                    else
                    {
                        var error = systemResult.Error ?? new InvalidOperationException("Unknown system diagnostics error");
                        Logger.LogWarning(error, "Failed to refresh system diagnostics snapshot.");
                        errorMessages.Add("Unable to retrieve system diagnostics.");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Unexpected error refreshing system diagnostics snapshot.");
                    errorMessages.Add("Unexpected error while retrieving system diagnostics.");
                }
                    _lastSystemRefreshUtc = nowUtc;
            }

            _errorMessage = errorMessages.Count > 0 ? string.Join(" ", errorMessages) : null;
            _isLoading = false;
        }
        finally
        {
            _refreshLock.Release();
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            await InvokeAsync(StateHasChanged);
        }
    }

    private void ApplyHistory(IReadOnlyList<BackgroundStackerHistorySample> samples)
    {
        _queueFillHistory.Clear();
        _queueLatencyHistory.Clear();
        _stackDurationHistory.Clear();

        if (samples.Count == 0)
        {
            return;
        }

        foreach (var sample in samples)
        {
            UpdateHistory(_queueFillHistory, sample.QueueFillPercentage);
            UpdateHistory(_queueLatencyHistory, sample.QueueLatencyMilliseconds ?? 0d);
            UpdateHistory(_stackDurationHistory, sample.StackDurationMilliseconds ?? 0d);
        }
    }

    private static void UpdateHistory(List<double> history, double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            value = 0d;
        }

        history.Add(value);
        if (history.Count > HistoryCapacity)
        {
            history.RemoveAt(0);
        }
    }

    private static string BuildGaugeStyle(double percentage)
    {
        var clamped = Math.Clamp(double.IsNaN(percentage) ? 0d : percentage, 0d, 100d);
        var color = GetGaugeColor(clamped);
        return $"--value:{clamped:F1};--gauge-color:{color};";
    }

    private static string GetCpuCoreBarStyle(double usagePercent)
    {
        var clamped = Math.Clamp(double.IsNaN(usagePercent) ? 0d : usagePercent, 0d, 100d);
        var color = GetGaugeColor(clamped);
        return $"width:{clamped:F1}%;background:{color};";
    }

    private static string GetGaugeColor(double percentage) => percentage switch
    {
        >= 90 => "#dc3545",
        >= 75 => "#fd7e14",
        >= 55 => "#ffc107",
        >= 30 => "#0dcaf0",
        _ => "#198754"
    };

    private static double? CalculateMemoryUsagePercent(MemoryUsageSnapshot snapshot)
    {
        if (snapshot.TotalMegabytes.HasValue && snapshot.TotalMegabytes.Value > 0 && snapshot.UsedMegabytes.HasValue)
        {
            var percent = snapshot.UsedMegabytes.Value / snapshot.TotalMegabytes.Value * 100d;
            return Math.Clamp(percent, 0d, 100d);
        }

        return null;
    }

    private static string FormatPercent(double value) => $"{value:F1}%";

    private static string FormatPercent(double? value) => value.HasValue ? FormatPercent(value.Value) : "—";

    private static string FormatDepth(int depth, int capacity) => capacity > 0
        ? $"{depth.ToString("N0", CultureInfo.CurrentCulture)} / {capacity.ToString("N0", CultureInfo.CurrentCulture)}"
        : depth.ToString("N0", CultureInfo.CurrentCulture);

    private static string FormatCount(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    private static string FormatMemory(double megabytes)
    {
        if (double.IsNaN(megabytes) || double.IsInfinity(megabytes))
        {
            return "—";
        }

        return string.Create(CultureInfo.CurrentCulture, $"{megabytes:F2} MB");
    }

    private static string FormatMilliseconds(double? value) => value.HasValue
        ? string.Create(CultureInfo.CurrentCulture, $"{value.Value:F1} ms")
        : "—";

    private static string FormatSeconds(double? value)
    {
        if (!value.HasValue)
        {
            return "—";
        }

        var seconds = Math.Max(0d, value.Value);

        if (seconds < 1)
        {
            return string.Create(CultureInfo.CurrentCulture, $"{seconds:F3} s");
        }

        if (seconds < 10)
        {
            return string.Create(CultureInfo.CurrentCulture, $"{seconds:F2} s");
        }

        return string.Create(CultureInfo.CurrentCulture, $"{seconds:F1} s");
    }

    private static string FormatMegabytes(double? value)
    {
        if (!value.HasValue)
        {
            return "—";
        }

        return FormatMemory(value.Value);
    }

    private static string FormatDuration(double? seconds)
    {
        if (!seconds.HasValue)
        {
            return "—";
        }

        var duration = TimeSpan.FromSeconds(Math.Max(0d, seconds.Value));

        if (duration.TotalHours >= 1d)
        {
            return string.Create(CultureInfo.CurrentCulture, $"{(int)duration.TotalHours}h {duration.Minutes:D2}m");
        }

        if (duration.TotalMinutes >= 1d)
        {
            return string.Create(CultureInfo.CurrentCulture, $"{duration.Minutes:D2}m {duration.Seconds:D2}s");
        }

        return string.Create(CultureInfo.CurrentCulture, $"{duration.Seconds:D2}s");
    }

    private static string BuildHistoryDurationLabel(int sampleCount)
    {
        if (sampleCount <= 1)
        {
            return "Rolling window < 10 s";
        }

    var cadenceSeconds = QueueRefreshInterval.TotalSeconds;
    var totalSeconds = sampleCount * cadenceSeconds;
        if (totalSeconds >= 90)
        {
            var minutes = totalSeconds / 60d;
            return string.Create(CultureInfo.CurrentCulture, $"Rolling window {minutes:F1} min");
        }

        return string.Create(CultureInfo.CurrentCulture, $"Rolling window {totalSeconds:F0} s");
    }

    private static string BuildMaxLabel(IReadOnlyCollection<double> values, string unit)
    {
        if (values.Count == 0)
        {
            return "No samples yet";
        }

        var max = values.Max();
        return string.Create(CultureInfo.CurrentCulture, $"Max {max:F1} {unit}");
    }

    private static string DescribeQueuePressure(int level) => level switch
    {
        <= 0 => "Nominal",
        1 => "Rising",
        2 => "Elevated",
        _ => "High"
    };

    private string GetFilterBarStyle(FilterMetrics metric)
    {
        var max = _filterMetrics is { Filters.Count: > 0 }
            ? _filterMetrics.Filters.Max(x => x.AverageDurationMilliseconds ?? x.LastDurationMilliseconds ?? 0d)
            : 0d;

        if (max <= 0)
        {
            max = 1d;
        }

        var baseline = metric.AverageDurationMilliseconds ?? metric.LastDurationMilliseconds ?? 0d;
        var percent = Math.Clamp((baseline / max) * 100d, 5d, 100d);
        var color = baseline switch
        {
            >= 20 => "#dc3545",
            >= 10 => "#fd7e14",
            >= 5 => "#ffc107",
            >= 2 => "#0dcaf0",
            _ => "#0d6efd"
        };

        return $"width:{percent:F1}%;background:{color};";
    }

    private enum DiagnosticsTab
    {
        System,
        Filters,
        Queue
    }

    private bool ShouldRefreshQueueMetrics() => ActiveTab is DiagnosticsTab.Queue or DiagnosticsTab.System;

    private bool ShouldRefreshFilterMetrics() => ActiveTab is DiagnosticsTab.Filters or DiagnosticsTab.System;

    private bool ShouldRefreshSystemMetrics() => ActiveTab is DiagnosticsTab.System;
}
