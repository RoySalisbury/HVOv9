using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Cameras.Optics;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Storage;
using Microsoft.AspNetCore.Components;

namespace HVO.SkyMonitorV5.RPi.Components.Pages;

/// <summary>
/// Displays the latest SkyMonitor v5 imagery and capture status.
/// </summary>
public sealed partial class Home : ComponentBase, IDisposable
{
    private readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(10);

    private AllSkyStatusResponse? _status;
    private PeriodicTimer? _refreshTimer;
    private CancellationTokenSource? _refreshCts;
    private Task? _refreshTask;
    private string _cacheBuster = string.Empty;
    private int _configurationVersion;

    [Inject]
    public IFrameStateStore FrameStateStore { get; set; } = default!;

    [Inject]
    public ILogger<Home> Logger { get; set; } = default!;

    [Inject]
    public IObservatoryClock ObservatoryClock { get; set; } = default!;

    protected override void OnInitialized()
    {
        UpdateStatus();

        _refreshCts = new CancellationTokenSource();
        _refreshTimer = new PeriodicTimer(_refreshInterval);
        _refreshTask = RunRefreshLoopAsync(_refreshCts.Token);
    }

    public void Dispose()
    {
        try
        {
            _refreshCts?.Cancel();
            _refreshTimer?.Dispose();
        }
        finally
        {
            _refreshCts?.Dispose();
        }
    }

    private async Task RunRefreshLoopAsync(CancellationToken cancellationToken)
    {
        if (_refreshTimer is null)
        {
            return;
        }

        try
        {
            while (await _refreshTimer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var previousTimestamp = _status?.LastFrameTimestamp;
                UpdateStatus();

                if (previousTimestamp != _status?.LastFrameTimestamp)
                {
                    Logger.LogTrace("Latest frame timestamp updated to {Timestamp}", _status?.LastFrameTimestamp);
                }

                await InvokeAsync(StateHasChanged).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during disposal.
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to refresh SkyMonitor v5 status in the UI.");
        }
    }

    private void UpdateStatus()
    {
        var statusSnapshot = FrameStateStore.GetStatus();
        _configurationVersion = FrameStateStore.ConfigurationVersion;

        var previousTimestamp = _status?.LastFrameTimestamp;
        _status = statusSnapshot;

        if (statusSnapshot.LastFrameTimestamp != previousTimestamp || string.IsNullOrEmpty(_cacheBuster))
        {
            var cacheSource = statusSnapshot.LastFrameTimestamp ?? ObservatoryClock.LocalNow;
            _cacheBuster = cacheSource.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        }
    }

    private string StatusText => _status?.IsRunning == true ? "Running" : "Standby";

    private string StatusBadgeClass => _status?.IsRunning == true ? "status-badge--running" : "status-badge--stopped";

    private string OverlayStatusText
    {
        get
        {
            var status = _status;
            if (status?.Configuration is not { } configuration)
            {
                return "Unknown";
            }

            if (!HasOverlaysEnabled(configuration))
            {
                return "Disabled";
            }

            var overlayLabel = HasMaskEnabled(configuration) ? "Image + mask" : "Image overlays";

            if (status.ProcessedFrame is not { } processed)
            {
                return FormattableString.Invariant($"{overlayLabel} · awaiting frame");
            }

            var frameText = processed.FramesStacked == 1
                ? "1 frame"
                : FormattableString.Invariant($"{processed.FramesStacked} frames");

            var integrationText = FormatIntegrationText(processed.IntegrationMilliseconds);

            return FormattableString.Invariant($"{overlayLabel} · {frameText} · {integrationText}");
        }
    }

    private static bool HasOverlaysEnabled(CameraConfiguration configuration)
        => configuration.EnableImageOverlays
            || HasMaskEnabled(configuration)
            || configuration.FrameFilters.Any(IsOverlayFilterName);

    private static bool HasMaskEnabled(CameraConfiguration configuration)
        => configuration.EnableCircularApertureMask
            || configuration.FrameFilters.Any(static filter => string.Equals(filter, FrameFilterNames.CircularApertureMask, StringComparison.OrdinalIgnoreCase));

    private static bool IsOverlayFilterName(string filterName)
        => string.Equals(filterName, FrameFilterNames.CardinalDirections, StringComparison.OrdinalIgnoreCase)
            || string.Equals(filterName, FrameFilterNames.CelestialAnnotations, StringComparison.OrdinalIgnoreCase)
            || string.Equals(filterName, FrameFilterNames.OverlayText, StringComparison.OrdinalIgnoreCase)
            || string.Equals(filterName, FrameFilterNames.CircularApertureMask, StringComparison.OrdinalIgnoreCase);

    private static string FormatIntegrationText(int integrationMilliseconds)
    {
        if (integrationMilliseconds <= 0)
        {
            return "0 ms";
        }

        if (integrationMilliseconds < 1_000)
        {
            return FormattableString.Invariant($"{integrationMilliseconds} ms");
        }

        var totalSeconds = integrationMilliseconds / 1_000;
        if (totalSeconds < 60)
        {
            return FormattableString.Invariant($"{totalSeconds} s");
        }

        var minutes = totalSeconds / 60;
        var secondsRemainder = totalSeconds % 60;

        return secondsRemainder == 0
            ? FormattableString.Invariant($"{minutes} min")
            : FormattableString.Invariant($"{minutes} min {secondsRemainder} s");
    }

    private string ExposureSummary
    {
        get
        {
            if (_status?.LastExposure is not { } exposure)
            {
                return "Awaiting capture";
            }

            return FormattableString.Invariant($"{exposure.ExposureMilliseconds} ms · Gain {exposure.Gain}");
        }
    }

    private string LastFrameTimestampText
    {
        get
        {
            if (_status?.LastFrameTimestamp is not { } timestamp)
            {
                return "Awaiting capture";
            }

            return ObservatoryClock.ToLocal(timestamp).ToString("MMM d, yyyy • h:mm:ss tt", CultureInfo.CurrentCulture);
        }
    }

    private string StackingSummary
    {
        get
        {
            if (_status?.Configuration is not { } configuration)
            {
                return "Not configured";
            }

            if (!configuration.EnableStacking)
            {
                return "Disabled";
            }

            return FormattableString.Invariant(
                $"Enabled · {configuration.StackingFrameCount} frame stack · Buffer ≥ {configuration.StackingBufferMinimumFrames} frames / {configuration.StackingBufferIntegrationSeconds}s");
        }
    }

    private string CameraCapabilitiesText
    {
        get
        {
            var camera = _status?.Summary?.Camera;
            if (camera?.Capabilities is { Count: > 0 } capabilities)
            {
                return string.Join(", ", capabilities);
            }

            return "Not reported";
        }
    }

    private string RigNameText => _status?.Summary?.Rig?.Name ?? "Not reported";

    private string SensorSummaryText
    {
        get
        {
            if (_status?.Summary?.Rig?.Sensor is not { } sensor)
            {
                return "Not reported";
            }

            return FormattableString.Invariant(
                $"{sensor.WidthPx} × {sensor.HeightPx} px · {sensor.PixelSizeMicrons:0.##} µm pixels");
        }
    }

    private string LensSummaryText
    {
        get
        {
            if (_status?.Summary?.Rig?.Lens is not { } lens)
            {
                return "Not reported";
            }

            var label = string.IsNullOrWhiteSpace(lens.Name) ? lens.Kind.ToString() : lens.Name;
            var fovY = lens.FovYDeg is double fovYDeg
                ? FormattableString.Invariant($"{lens.FovXDeg:0.#}° × {fovYDeg:0.#}°")
                : FormattableString.Invariant($"{lens.FovXDeg:0.#}°");

            return FormattableString.Invariant(
                $"{label} · {lens.FocalLengthMm:0.0} mm · {lens.Model} · FOV {fovY}");
        }
    }

    private IReadOnlyList<string> AppliedFilters => _status?.ProcessedFrame?.AppliedFilters ?? Array.Empty<string>();

    private string PipelineFiltersSummary
    {
        get
        {
            var applied = AppliedFilters;
            if (applied.Count > 0)
            {
                return string.Join(", ", applied);
            }

            var configured = _status?.Configuration?.FrameFilters;
            if (configured is { Count: > 0 })
            {
                return string.Join(", ", configured);
            }

            return "No filters (raw frame)";
        }
    }

    private string TotalIntegrationSummary
    {
        get
        {
            if (_status?.ProcessedFrame is not { } processed)
            {
                return "Awaiting capture";
            }

            return FormatIntegrationText(processed.IntegrationMilliseconds);
        }
    }

    private string PipelineProcessingSummary
    {
        get
        {
            if (_status?.ProcessedFrame is not { } processed)
            {
                return "Awaiting capture";
            }

            return FormatDurationText(processed.ProcessingMilliseconds);
        }
    }

    private BackgroundStackerStatus? BackgroundStackerStatus => _status?.BackgroundStacker;

    private string BackgroundWorkerSummary
    {
        get
        {
            if (BackgroundStackerStatus is not { } status)
            {
                return "Not available";
            }

            return status.Enabled
                ? FormattableString.Invariant($"Enabled · {status.ProcessedFrameCount} frames processed")
                : "Disabled";
        }
    }

    private string BackgroundQueueDepthSummary
    {
        get
        {
            if (BackgroundStackerStatus is not { } status)
            {
                return "Not available";
            }

            var capacity = Math.Max(1, status.QueueCapacity);
            var fill = status.QueueDepth / (double)capacity;

            return FormattableString.Invariant($"{status.QueueDepth}/{capacity} ({fill:P0})");
        }
    }

    private string BackgroundQueuePeakSummary
    {
        get
        {
            if (BackgroundStackerStatus is not { } status)
            {
                return "Not available";
            }

            var capacity = Math.Max(1, status.QueueCapacity);
            var peak = Math.Clamp(status.PeakQueueDepth, 0, capacity);

            return FormattableString.Invariant($"{peak}/{capacity}");
        }
    }

    private string BackgroundQueueMemorySummary
    {
        get
        {
            if (BackgroundStackerStatus is not { } status)
            {
                return "Not available";
            }

            var current = FormatBytes(status.QueueMemoryBytes);
            var peak = FormatBytes(status.PeakQueueMemoryBytes);

            return FormattableString.Invariant($"{current} (peak {peak})");
        }
    }

    private string BackgroundQueueLatencySummary
    {
        get
        {
            if (BackgroundStackerStatus is not { } status)
            {
                return "Not available";
            }

            var last = FormatLatency(status.LastQueueLatencyMilliseconds);
            var average = FormatLatency(status.AverageQueueLatencyMilliseconds);

            return FormattableString.Invariant($"Last {last} · Avg {average}");
        }
    }

    private string BackgroundProcessingAverageSummary
    {
        get
        {
            if (BackgroundStackerStatus is not { } status)
            {
                return "Not available";
            }

            var stack = FormatLatency(status.AverageStackMilliseconds);
            var filter = FormatLatency(status.AverageFilterMilliseconds);

            return FormattableString.Invariant($"Stack {stack} · Filter {filter}");
        }
    }

    private string BackgroundDropSummary
    {
        get
        {
            if (BackgroundStackerStatus is not { } status)
            {
                return "Not available";
            }

            return status.DroppedFrameCount > 0
                ? FormattableString.Invariant($"{status.DroppedFrameCount} total")
                : "None";
        }
    }

    private string BackgroundLastCompletedSummary
    {
        get
        {
            if (BackgroundStackerStatus?.LastCompletedAt is not { } timestamp)
            {
                return "—";
            }

            return ObservatoryClock.ToLocal(timestamp).ToString("h:mm:ss tt", CultureInfo.CurrentCulture);
        }
    }

    private string FramesStackedSummary
    {
        get
        {
            if (_status?.ProcessedFrame is not { } processed)
            {
                return "Awaiting capture";
            }

            return processed.FramesStacked == 1
                ? "1 frame"
                : FormattableString.Invariant($"{processed.FramesStacked} frames");
        }
    }

    private string ConfigurationVersion => _configurationVersion > 0 ? $"#{_configurationVersion}" : "—";

    private string? ProcessedImageUrl => BuildImageUrl(raw: false);

    private string? RawImageUrl => BuildImageUrl(raw: true);

    private string? BuildImageUrl(bool raw)
    {
        if (_status?.LastFrameTimestamp is null)
        {
            return null;
        }

        var cacheKey = string.IsNullOrWhiteSpace(_cacheBuster)
            ? ObservatoryClock.LocalNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)
            : _cacheBuster;

        return FormattableString.Invariant($"api/v1.0/all-sky/frame/latest?raw={(raw ? "true" : "false")}&cacheBust={cacheKey}");
    }

    private static string FormatDurationText(int milliseconds)
    {
        if (milliseconds <= 0)
        {
            return "0 ms";
        }

        if (milliseconds < 1_000)
        {
            return FormattableString.Invariant($"{milliseconds} ms");
        }

        var totalSeconds = milliseconds / 1_000d;

        if (totalSeconds < 60)
        {
            return FormattableString.Invariant($"{totalSeconds:0.0} s");
        }

        var minutes = Math.Floor(totalSeconds / 60);
        var seconds = totalSeconds % 60;

        return seconds < 0.1
            ? FormattableString.Invariant($"{minutes:0} min")
            : FormattableString.Invariant($"{minutes:0} min {seconds:0.0} s");
    }

    private static string FormatLatency(double? milliseconds)
    {
        if (milliseconds is null)
        {
            return "—";
        }

        if (milliseconds.Value <= 0)
        {
            return "0 ms";
        }

        if (milliseconds.Value < 1)
        {
            return FormattableString.Invariant($"{milliseconds.Value * 1_000:0.0} µs");
        }

        if (milliseconds.Value < 1_000)
        {
            return FormattableString.Invariant($"{milliseconds.Value:0.0} ms");
        }

        return FormattableString.Invariant($"{milliseconds.Value / 1_000:0.00} s");
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

    string[] units = { "B", "KiB", "MiB", "GiB", "TiB" };
        double value = bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? FormattableString.Invariant($"{bytes} {units[unitIndex]}")
            : FormattableString.Invariant($"{value:0.00} {units[unitIndex]}");
    }
}
