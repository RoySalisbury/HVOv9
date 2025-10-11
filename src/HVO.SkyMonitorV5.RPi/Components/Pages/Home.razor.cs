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

    private ExposureAnalysisSummary? ExposureAnalysis => _status?.ExposureAnalysis ?? _status?.Summary?.ExposureAnalysis;

    private ExposureOverrideSnapshot? DayExposureOverride => _status?.ExposureOverrides?.Day;

    private ExposureOverrideSnapshot? NightExposureOverride => _status?.ExposureOverrides?.Night;

    private string ExposureAnalysisTimestampText
    {
        get
        {
            if (ExposureAnalysis?.Timestamp is not { } timestamp)
            {
                return "—";
            }

            return ObservatoryClock.ToLocal(timestamp).ToString("MMM d, yyyy • h:mm:ss tt", CultureInfo.CurrentCulture);
        }
    }

    private string ExposureLightingDescription
    {
        get
        {
            if (ExposureAnalysis is not { } analysis)
            {
                return "Awaiting analysis";
            }

            return analysis.LightingCondition switch
            {
                ExposureLightingCondition.Daylight => "Daylight scene",
                ExposureLightingCondition.Twilight => "Twilight scene",
                ExposureLightingCondition.Night => "Night scene",
                _ => "Unknown scene"
            };
        }
    }

    private string ExposureLuminanceSummary
    {
        get
        {
            if (ExposureAnalysis is not { } analysis)
            {
                return "Awaiting analysis";
            }

            var metrics = (Average: analysis.AverageLuminance, Minimum: analysis.MinimumLuminance, Maximum: analysis.MaximumLuminance);
            var culture = CultureInfo.CurrentCulture;
            var sampleText = analysis.SampleCount > 0
                ? analysis.SampleCount.ToString("N0", culture)
                : "0";

            return string.Format(
                culture,
                "Avg {0:0.0} · Min {1:0.0} · Max {2:0.0} ({3} samples)",
                metrics.Average,
                metrics.Minimum,
                metrics.Maximum,
                sampleText);
        }
    }

    private string ExposureRecommendationSummary
    {
        get
        {
            if (ExposureAnalysis is not { } analysis)
            {
                return "Awaiting analysis";
            }

            var exposureMs = analysis.SuggestedExposureMilliseconds;
            var gain = analysis.SuggestedGain;
            var hasExposure = exposureMs.HasValue;
            var hasGain = gain.HasValue;

            if (!hasExposure && !hasGain)
            {
                return "Maintain current settings";
            }

            return hasExposure && hasGain
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    "Adjust to {0} ms · Gain {1}",
                    exposureMs!.Value,
                    gain!.Value)
                : hasExposure
                    ? string.Format(
                        CultureInfo.CurrentCulture,
                        "Adjust exposure to {0} ms",
                        exposureMs!.Value)
                    : string.Format(
                        CultureInfo.CurrentCulture,
                        "Adjust gain to {0}",
                        gain!.Value);
        }
    }

    private string? ExposureAnalysisNotes
    {
        get
        {
            var notes = ExposureAnalysis?.Notes;
            return string.IsNullOrWhiteSpace(notes) ? null : notes;
        }
    }

    private string DayExposureOverrideSummary => FormatOverrideSummary(DayExposureOverride, "Day");

    private string NightExposureOverrideSummary => FormatOverrideSummary(NightExposureOverride, "Night");

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

    private string PipelineCapabilitiesText
    {
        get
        {
            var pipeline = _status?.Summary?.Camera?.Capabilities;
            if (pipeline is { Count: > 0 })
            {
                return string.Join(", ", pipeline);
            }

            return "Not reported";
        }
    }

    private string CameraHardwareCapabilitiesText
    {
        get
        {
            var hardware = _status?.Summary?.Camera?.HardwareCapabilities;
            if (hardware is { Count: > 0 })
            {
                return string.Join(", ", hardware);
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

    private CapturePacingStatus? CapturePacing => _status?.CapturePacing ?? _status?.Summary?.CapturePacing;

    private ProcessingQueueStatus? ProcessingQueue => _status?.ProcessingQueue ?? _status?.Summary?.ProcessingQueue;

    private string CapturePacingSummary
    {
        get
        {
            if (CapturePacing is not { } pacing)
            {
                return "Telemetry unavailable";
            }

            if (!pacing.Enabled)
            {
                return "Disabled";
            }

            if (!pacing.UsingBackgroundStacker)
            {
                return "Bypassed (background stacker off)";
            }

            var penaltyText = pacing.PenaltyAdditionalDelayMilliseconds > 0
                ? FormattableString.Invariant($", penalty +{pacing.PenaltyAdditionalDelayMilliseconds} ms")
                : string.Empty;

            return FormattableString.Invariant(
                $"{pacing.AdjustedDelayMilliseconds} ms (base {pacing.BaseDelayMilliseconds} ms, pressure +{pacing.PressureAdditionalDelayMilliseconds} ms{penaltyText})");
        }
    }

    private string CapturePacingPressureSummary
    {
        get
        {
            if (CapturePacing is not { } pacing || !pacing.Enabled || !pacing.UsingBackgroundStacker)
            {
                return "—";
            }

            var descriptor = pacing.QueuePressureLevel switch
            {
                3 => "Critical",
                2 => "High",
                1 => "Elevated",
                _ => "Normal"
            };

            return FormattableString.Invariant($"{descriptor} ({pacing.QueuePressureLevel}/3)");
        }
    }

    private string CapturePacingPenaltySummary
    {
        get
        {
            if (CapturePacing is not { } pacing || !pacing.Enabled || !pacing.UsingBackgroundStacker)
            {
                return "Penalty inactive";
            }

            if (pacing.PenaltyActive)
            {
                if (pacing.PenaltyExpiresAt is { } expires)
                {
                    var expiresText = expires.ToString("h:mm:ss tt", CultureInfo.CurrentCulture);
                    return FormattableString.Invariant(
                        $"Active (+{pacing.PenaltyAdditionalDelayMilliseconds} ms) until {expiresText}");
                }

                return FormattableString.Invariant(
                    $"Active (+{pacing.PenaltyAdditionalDelayMilliseconds} ms)");
            }

            return pacing.PenaltyAdditionalDelayMilliseconds > 0
                ? FormattableString.Invariant($"Cooling (+{pacing.PenaltyAdditionalDelayMilliseconds} ms)")
                : "Penalty inactive";
        }
    }

    private string CapturePacingUpdatedSummary
    {
        get
        {
            if (CapturePacing?.Timestamp is not { } timestamp)
            {
                return "—";
            }

            return timestamp.ToString("h:mm:ss tt", CultureInfo.CurrentCulture);
        }
    }

    private string ProcessingQueueSummary
    {
        get
        {
            if (ProcessingQueue is not { } queue)
            {
                return "Telemetry unavailable";
            }

            if (!queue.Enabled)
            {
                return "Disabled";
            }

            var capacity = Math.Max(1, queue.Capacity);
            var depth = Math.Clamp(queue.Depth, 0, capacity);
            var fillPercentage = capacity > 0 ? depth / (double)capacity : 0;

            return FormattableString.Invariant($"{depth}/{capacity} ({fillPercentage:P0})");
        }
    }

    private string ProcessingQueueBackpressureSummary
    {
        get
        {
            if (ProcessingQueue is not { } queue || !queue.Enabled)
            {
                return "—";
            }

            var last = FormatLatency(queue.LastEnqueueWaitMilliseconds);
            var peak = FormatLatency(queue.PeakEnqueueWaitMilliseconds);

            return queue.BackpressureEvents > 0
                ? FormattableString.Invariant($"{queue.BackpressureEvents} events · last {last} · peak {peak}")
                : FormattableString.Invariant($"None · last {last}");
        }
    }

    private string ProcessingQueueProcessingSummary
    {
        get
        {
            if (ProcessingQueue is not { } queue || !queue.Enabled)
            {
                return "—";
            }

            var last = FormatLatency(queue.LastProcessingMilliseconds);
            var average = FormatLatency(queue.AverageProcessingMilliseconds);
            var peak = FormatLatency(queue.PeakProcessingMilliseconds);

            return FormattableString.Invariant($"Last {last} · Avg {average} · Peak {peak}");
        }
    }

    private string ProcessingQueueUpdatedSummary
    {
        get
        {
            if (ProcessingQueue?.Timestamp is not { } timestamp)
            {
                return "—";
            }

            return timestamp.ToString("h:mm:ss tt", CultureInfo.CurrentCulture);
        }
    }

    private RemoteDispatchStatus? RemoteDispatchStatusData
        => _status?.RemoteDispatch ?? _status?.Summary?.RemoteDispatch;

    private string RemoteDispatchModeSummary
        => RemoteDispatchStatusData is { } status
            ? status.Mode
            : "Disabled";

    private string RemoteDispatchOutcomeSummary
    {
        get
        {
            if (RemoteDispatchStatusData is not { } status)
            {
                return "Dispatch inactive";
            }

            return status.Outcome switch
            {
                RemoteDispatchOutcome.Succeeded => "Published",
                RemoteDispatchOutcome.Skipped => "Queued",
                RemoteDispatchOutcome.Disabled => "Disabled",
                RemoteDispatchOutcome.Failed => "Failed",
                _ => status.Outcome.ToString()
            };
        }
    }

    private string RemoteDispatchCapturedSummary
    {
        get
        {
            if (RemoteDispatchStatusData?.CapturedAtLocal is not { } timestamp)
            {
                return "—";
            }

            return FormatTimestamp(timestamp);
        }
    }

    private string? RemoteDispatchMessage => RemoteDispatchStatusData?.Message;

    private string? RemoteDispatchError => RemoteDispatchStatusData?.ErrorMessage;

    private RemoteDispatchMetricsSnapshot? RemoteDispatchMetrics
    {
        get
        {
            if (RemoteDispatchStatusData?.Metrics is { } statusMetrics)
            {
                return statusMetrics;
            }

            return FrameStateStore.RemoteDispatchMetrics;
        }
    }

    private bool HasRemoteDispatchTelemetry => RemoteDispatchMetrics is { SampleCount: > 0 };

    private string RemoteDispatchSuccessRateSummary
        => HasRemoteDispatchTelemetry
            ? FormatPercent(RemoteDispatchMetrics!.SuccessRatePercent)
            : "—";

    private string RemoteDispatchAttemptsSummary
    {
        get
        {
            if (!HasRemoteDispatchTelemetry)
            {
                return "No attempts recorded";
            }

            var metrics = RemoteDispatchMetrics!;

            return FormattableString.Invariant(
                $"{FormatCount(metrics.SampleCount)} attempts · {FormatCount(metrics.SuccessCount)} ok · {FormatCount(metrics.FailureCount)} failed · {FormatCount(metrics.SkippedCount)} skipped");
        }
    }

    private string RemoteDispatchLatencySummary
    {
        get
        {
            if (!HasRemoteDispatchTelemetry)
            {
                return "—";
            }

            var metrics = RemoteDispatchMetrics!;
            var average = FormatLatency(metrics.AverageLatencyMilliseconds);
            var peak = FormatLatency(metrics.PeakLatencyMilliseconds);

            return FormattableString.Invariant($"Avg {average} · Peak {peak}");
        }
    }

    private string RemoteDispatchLastLatencySummary => FormatLatency(RemoteDispatchMetrics?.LastLatencyMilliseconds);

    private string RemoteDispatchLastPayloadSummary
    {
        get
        {
            if (!HasRemoteDispatchTelemetry)
            {
                return "—";
            }

            var metrics = RemoteDispatchMetrics!;

            if (metrics.LastPayloadBytes is not { } bytes || bytes <= 0)
            {
                return "—";
            }

            var descriptor = FormatBytes(bytes);

            if (!string.IsNullOrWhiteSpace(metrics.LastPayloadExtension))
            {
                descriptor = FormattableString.Invariant($"{descriptor} · .{metrics.LastPayloadExtension}");
            }

            if (!string.IsNullOrWhiteSpace(metrics.LastPayloadContentType))
            {
                descriptor = FormattableString.Invariant($"{descriptor} · {metrics.LastPayloadContentType}");
            }

            return descriptor;
        }
    }

    private IReadOnlyList<RemoteDispatchFormatSummary> RemoteDispatchFormatSummaries
        => RemoteDispatchMetrics?.FormatCounts ?? Array.Empty<RemoteDispatchFormatSummary>();

    private string RemoteDispatchFormatsSummary
    {
        get
        {
            if (!HasRemoteDispatchTelemetry || RemoteDispatchFormatSummaries.Count == 0)
            {
                return string.Empty;
            }

            var topFormats = RemoteDispatchFormatSummaries
                .OrderByDescending(static f => f.Count)
                .ThenBy(static f => f.FormatKey, StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .Select(static f => FormattableString.Invariant($"{f.FormatKey} {FormatCount(f.Count)}"));

            return string.Join(" · ", topFormats);
        }
    }

    private string RemoteDispatchTelemetryUpdatedSummary
    {
        get
        {
            if (RemoteDispatchMetrics is not { GeneratedAt: var timestamp })
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

    private static string FormatPercent(double value)
        => string.Format(CultureInfo.CurrentCulture, "{0:0.0}%", value);

    private static string FormatPercent(double? value)
        => value.HasValue ? FormatPercent(value.Value) : "—";

    private static string FormatCount(int value)
        => value.ToString("N0", CultureInfo.CurrentCulture);

    private static string FormatCount(long value)
        => value.ToString("N0", CultureInfo.CurrentCulture);

    private string FormatOverrideSummary(ExposureOverrideSnapshot? snapshot, string label)
    {
        if (snapshot is null)
        {
            return FormattableString.Invariant($"{label}: Inactive");
        }

        var applied = FormattableString.Invariant($"{snapshot.AppliedExposureMilliseconds} ms · Gain {snapshot.AppliedGain}");
        var target = FormattableString.Invariant($"{snapshot.TargetExposureMilliseconds} ms · Gain {snapshot.TargetGain}");
        var baseline = FormattableString.Invariant($"{snapshot.BaselineExposureMilliseconds} ms · Gain {snapshot.BaselineGain}");
        var expiresText = snapshot.ExpiresAt is { } expires ? FormatTimeRemaining(expires) : "—";
        var updatedText = snapshot.LastUpdated is { } updated ? FormatTimestamp(updated) : "—";

        return FormattableString.Invariant($"{label}: {applied} (target {target}, baseline {baseline}) · set {updatedText} · expires {expiresText}");
    }

    private string FormatTimeRemaining(DateTimeOffset expiresAt)
    {
        var now = ObservatoryClock.LocalNow;
        var remaining = expiresAt - now;

        if (remaining <= TimeSpan.Zero)
        {
            return "now";
        }

        if (remaining.TotalMinutes >= 1)
        {
            var minutes = (int)remaining.TotalMinutes;
            var seconds = Math.Max(0, remaining.Seconds);
            return seconds == 0
                ? FormattableString.Invariant($"in {minutes} min")
                : FormattableString.Invariant($"in {minutes} min {seconds} s");
        }

        return FormattableString.Invariant($"in {remaining.TotalSeconds:0}s");
    }

    private string FormatTimestamp(DateTimeOffset timestamp)
        => timestamp.ToString("h:mm:ss tt", CultureInfo.CurrentCulture);
}
