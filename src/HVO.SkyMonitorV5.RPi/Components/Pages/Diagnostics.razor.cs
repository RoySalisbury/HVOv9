using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Components.Pages;

public sealed partial class Diagnostics : ComponentBase, IAsyncDisposable
{
    private const int HistoryCapacity = 60;
    private const int RefreshIntervalSeconds = 5;

    private readonly List<double> _queueFillHistory = new();
    private readonly List<double> _queueLatencyHistory = new();
    private readonly List<double> _stackDurationHistory = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private CancellationTokenSource? _refreshCts;
    private Task? _refreshTask;
    private BackgroundStackerMetricsResponse? _stackerMetrics;
    private FilterMetricsSnapshot? _filterMetrics;
    private DateTimeOffset? _lastUpdated;
    private string? _errorMessage;
    private bool _isLoading = true;

    [Inject]
    private IDiagnosticsService DiagnosticsService { get; set; } = default!;

    [Inject]
    private ILogger<Diagnostics> Logger { get; set; } = default!;

    private bool IsLoading => _isLoading;
    private BackgroundStackerMetricsResponse? StackerMetrics => _stackerMetrics;
    private FilterMetricsSnapshot? FilterMetricsSnapshot => _filterMetrics;
    private string? ErrorMessage => _errorMessage;
    private string? LastUpdatedDisplay => _lastUpdated?.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);
    private string QueueFillGaugeStyle => BuildGaugeStyle(_stackerMetrics?.QueueFillPercentage ?? 0);
    private string QueueFillPercentageDisplay => _stackerMetrics is { } metrics ? FormatPercent(metrics.QueueFillPercentage) : "—";
    private string QueueDepthSummary => _stackerMetrics is { } metrics ? FormatDepth(metrics.QueueDepth, metrics.QueueCapacity) : "—";
    private string PeakQueueDepthSummary => _stackerMetrics is { } metrics ? FormatCount(metrics.PeakQueueDepth) : "—";
    private string PeakQueueFillDisplay => _stackerMetrics is { } metrics ? FormatPercent(metrics.PeakQueueFillPercentage) : "—";
    private string QueuePressureDisplay => _stackerMetrics is { } metrics ? DescribeQueuePressure(metrics.QueuePressureLevel) : "—";
    private string SecondsSinceLastCompletedDisplay => FormatSeconds(_stackerMetrics?.SecondsSinceLastCompleted);
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

    private string QueueFillHistoryPoints => BuildHistoryPolyline(_queueFillHistory, 100);
    private string QueueLatencyHistoryPoints => BuildHistoryPolyline(_queueLatencyHistory, GetHistoryMax(_queueLatencyHistory));
    private string StackDurationHistoryPoints => BuildHistoryPolyline(_stackDurationHistory, GetHistoryMax(_stackDurationHistory));

    protected override async Task OnInitializedAsync()
    {
        _refreshCts = new CancellationTokenSource();
        await RefreshAsync(_refreshCts.Token);
        _refreshTask = RunRefreshLoopAsync(_refreshCts.Token);
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
                await Task.Delay(TimeSpan.FromSeconds(RefreshIntervalSeconds), cancellationToken);
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

            try
            {
                var stackerResult = await DiagnosticsService.GetBackgroundStackerMetricsAsync(cancellationToken).ConfigureAwait(false);
                if (stackerResult.IsSuccessful)
                {
                    var metrics = stackerResult.Value;
                    latestMetrics = metrics;
                    _stackerMetrics = metrics;
                    _lastUpdated = DateTimeOffset.UtcNow;
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

    private static string GetGaugeColor(double percentage) => percentage switch
    {
        >= 90 => "#dc3545",
        >= 75 => "#fd7e14",
        >= 55 => "#ffc107",
        >= 30 => "#0dcaf0",
        _ => "#198754"
    };

    private static string FormatPercent(double value) => $"{value:F1}%";

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

        if (value.Value < 1)
        {
            return "< 1 s";
        }

        return string.Create(CultureInfo.CurrentCulture, $"{value.Value:F1} s");
    }

    private static string BuildHistoryDurationLabel(int sampleCount)
    {
        if (sampleCount <= 1)
        {
            return "Rolling window < 10 s";
        }

        var totalSeconds = sampleCount * RefreshIntervalSeconds;
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

    private static double GetHistoryMax(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0)
        {
            return 0d;
        }

        var max = values.Max();
        return Math.Max(1d, max);
    }

    private static string BuildHistoryPolyline(IReadOnlyList<double> values, double maxValue)
    {
        if (values.Count == 0)
        {
            return string.Empty;
        }

        var effectiveMax = Math.Max(1d, maxValue);
        var step = values.Count > 1 ? 100d / (values.Count - 1) : 100d;
        var builder = new StringBuilder(values.Count * 8);

        for (var index = 0; index < values.Count; index++)
        {
            var x = step * index;
            var normalized = Math.Clamp(values[index] / effectiveMax, 0d, 1d);
            var y = 40d - (normalized * 40d);

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(x.ToString("F1", CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(y.ToString("F1", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
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
}
