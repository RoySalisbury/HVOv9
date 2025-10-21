using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Cameras.Optics;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Services;
using HVO.SkyMonitorV5.RPi.Storage;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Components.Pages;

/// <summary>
/// Displays the latest SkyMonitor v5 imagery and capture status.
/// </summary>
public sealed partial class Monitor : ComponentBase, IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);
    private AllSkyStatusResponse? _status;
    private CancellationTokenSource? _refreshCts;
    private Task? _refreshTask;
    private Guid? _processedFrameId;
    private Guid? _rawFrameId;
    private string? _processedImageSource;
    private string? _processedFrameDetailUri;
    private string? _rawFrameDetailUri;
    private string? _rawImageDisplaySource;
    private int _configurationVersion;
    private bool _disposed;

    [Inject]
    public IFrameStateStore FrameStateStore { get; set; } = default!;

    [Inject]
    public ILogger<Monitor> Logger { get; set; } = default!;

    [Inject]
    public IObservatoryClock ObservatoryClock { get; set; } = default!;

    [Inject]
    public IFrameMediaProvider FrameMediaProvider { get; set; } = default!;

    protected override void OnInitialized()
    {
        base.OnInitialized();

        _ = RefreshStatusAsync(CancellationToken.None);
        StartRefreshLoop();
    }

    private void StartRefreshLoop()
    {
        StopRefreshLoop();

        _refreshCts = new CancellationTokenSource();
        _refreshTask = RunRefreshLoopAsync(_refreshCts.Token);
    }

    private void StopRefreshLoop()
    {
        var cts = Interlocked.Exchange(ref _refreshCts, null);
        var task = Interlocked.Exchange(ref _refreshTask, null);

        if (cts is not null)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Cancellation token source already disposed.
            }
        }

        cts?.Dispose();

        if (task is not null)
        {
            _ = task.ContinueWith(static t =>
            {
                if (t.IsFaulted && t.Exception is { } ex)
                {
                    _ = ex; // observe to avoid unobserved exception noise
                }
            }, TaskScheduler.Default);
        }
    }

    private async Task RunRefreshLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await RefreshStatusAsync(cancellationToken).ConfigureAwait(false);

                if (!await CancellationTokenHelpers.DelayWithoutThrowAsync(RefreshInterval, cancellationToken).ConfigureAwait(false))
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            Logger.LogError(ex, "Monitor refresh loop encountered an unexpected error.");
        }
    }

    private async Task RefreshStatusAsync(CancellationToken cancellationToken)
    {
        await TryRefreshStatusAsync(cancellationToken).ConfigureAwait(false);

        if (!cancellationToken.IsCancellationRequested && !_disposed)
        {
            await InvokeAsync(StateHasChanged).ConfigureAwait(false);
        }
    }

    private async Task TryRefreshStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshStatusCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during shutdown.
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to refresh monitor status.");
        }
    }

    private async Task RefreshStatusCoreAsync(CancellationToken cancellationToken)
    {
        _status = FrameStateStore.GetStatus();
        _configurationVersion = FrameStateStore.ConfigurationVersion;

        var processed = FrameStateStore.LatestProcessedFrame;
        await UpdateProcessedFrameAsync(processed, cancellationToken).ConfigureAwait(false);

        var raw = FrameStateStore.LatestRawFrame;
        await UpdateRawFrameAsync(raw, cancellationToken).ConfigureAwait(false);
    }

    private async Task UpdateProcessedFrameAsync(ProcessedFrame? processedFrame, CancellationToken cancellationToken)
    {
        if (processedFrame is null)
        {
            _processedFrameId = null;
            _processedImageSource = null;
            _processedFrameDetailUri = null;
            return;
        }

        if (_processedFrameId == processedFrame.FrameId && !string.IsNullOrWhiteSpace(_processedImageSource))
        {
            return;
        }

        FrameMedia? media = null;

        try
        {
            media = await FrameMediaProvider.GetProcessedFrameAsync(processedFrame.FrameId, processedFrame.Timestamp, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(
                ex,
                "Failed to retrieve processed frame {FrameId} from the local API for the monitor view.",
                processedFrame.FrameId);
        }

        if (media is null || string.IsNullOrWhiteSpace(media.DataUri))
        {
            Logger.LogWarning(
                "Processed frame {FrameId} produced no payload for the monitor view.",
                processedFrame.FrameId);
            _processedImageSource = null;
            _processedFrameId = null;
            _processedFrameDetailUri = null;
            return;
        }

        _processedImageSource = media.DataUri;
        _processedFrameId = processedFrame.FrameId;

        var detailExtension = !string.IsNullOrWhiteSpace(media.FileExtension)
            ? media.FileExtension
            : processedFrame.FileExtension ?? "png";

        _processedFrameDetailUri = BuildFrameDetailUrl(
            "processed-frame",
            processedFrame.FrameId,
            media.Timestamp,
            detailExtension);
    }

    private async Task UpdateRawFrameAsync(RawFrameSnapshot? rawFrame, CancellationToken cancellationToken)
    {
        if (rawFrame is null)
        {
            _rawFrameId = null;
            _rawImageDisplaySource = null;
            _rawFrameDetailUri = null;
            return;
        }

        if (_rawFrameId == rawFrame.FrameId && !string.IsNullOrWhiteSpace(_rawImageDisplaySource))
        {
            return;
        }

        FrameMedia? media = null;

        try
        {
            media = await FrameMediaProvider.GetRawFrameAsync(rawFrame.FrameId, rawFrame.Timestamp, RawFrameMediaFormat.Png, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(
                ex,
                "Failed to retrieve raw frame {FrameId} from the local API for the monitor view.",
                rawFrame.FrameId);
        }

        if (media is null || string.IsNullOrWhiteSpace(media.DataUri))
        {
            Logger.LogWarning(
                "Raw frame {FrameId} produced no payload for the monitor view.",
                rawFrame.FrameId);
            _rawFrameId = null;
            _rawImageDisplaySource = null;
            _rawFrameDetailUri = null;
            return;
        }

        _rawImageDisplaySource = media.DataUri;
        _rawFrameId = rawFrame.FrameId;

        var detailExtension = !string.IsNullOrWhiteSpace(media.FileExtension)
            ? media.FileExtension
            : "png";

        _rawFrameDetailUri = BuildFrameDetailUrl(
            "raw-frame",
            rawFrame.FrameId,
            media.Timestamp,
            detailExtension);
    }

    private string? ProcessedFrameDetailUri => _processedFrameDetailUri;

    private string? RawFrameDetailUri => _rawFrameDetailUri;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopRefreshLoop();
    }

    private CameraConfiguration? Configuration =>
        _status?.Configuration ?? _status?.Summary?.Configuration;

    private ProcessedFrameSummary? ProcessedFrameSummary =>
        _status?.ProcessedFrame ?? _status?.Summary?.ProcessedFrame;

    private RawFrameSummary? RawFrameSummary =>
        _status?.RawFrame ?? _status?.Summary?.RawFrame;

    private BackgroundStackerStatus? BackgroundStacker =>
        _status?.BackgroundStacker ?? _status?.Summary?.BackgroundStacker;

    private CapturePacingStatus? CapturePacing =>
        _status?.CapturePacing ?? _status?.Summary?.CapturePacing;

    private ProcessingQueueStatus? ProcessingQueue =>
        _status?.ProcessingQueue ?? _status?.Summary?.ProcessingQueue;

    private ExposureProfileSummary? ExposureProfiles =>
        _status?.ExposureProfiles ?? _status?.Summary?.ExposureProfiles;

    private ExposureAnalysisSummary? ExposureAnalysis =>
        _status?.ExposureAnalysis ?? _status?.Summary?.ExposureAnalysis;

    private ExposureOverrideSummary? ExposureOverrides =>
        _status?.ExposureOverrides ?? _status?.Summary?.ExposureOverrides;

    private ExposureOverrideSnapshot? DayExposureOverride => ExposureOverrides?.Day;

    private ExposureOverrideSnapshot? NightExposureOverride => ExposureOverrides?.Night;

    private AllSkyCameraSummary? CameraSummary => _status?.Summary?.Camera;

    private AllSkyRigSummary? RigSummary => _status?.Summary?.Rig;

    private AllSkySensorSummary? SensorSummary => RigSummary?.Sensor;

    private AllSkyLensSummary? LensSummary => RigSummary?.Lens;

    private RemoteDispatchStatus? RemoteDispatchStatusData =>
        _status?.RemoteDispatch ?? _status?.Summary?.RemoteDispatch;

    private string StatusBadgeClass => _status switch
    {
        { IsRunning: true } => "status-badge status-badge--running",
        { IsRunning: false } => "status-badge status-badge--stopped",
        _ => "status-badge"
    };

    private string StatusText
    {
        get
        {
            if (_status is null)
            {
                return "Waiting for telemetry";
            }

            var label = _status.IsRunning ? "Running" : "Stopped";
            if (_status.LastFrameTimestamp is not { } timestamp)
            {
                return label;
            }

            var local = ObservatoryClock.ToLocal(timestamp);
            return FormattableString.Invariant($"{label} · {local:HH:mm:ss}");
        }
    }

    private string OverlayStatusText
    {
        get
        {
            var configuration = Configuration;
            if (configuration is null)
            {
                return "Not configured";
            }

            if (!HasOverlaysEnabled(configuration))
            {
                return "Disabled";
            }

            var appliedOverlayFilters = AppliedFilters
                .Where(IsOverlayFilterName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (appliedOverlayFilters.Length == 0)
            {
                appliedOverlayFilters = configuration.FrameFilters
                    .Where(IsOverlayFilterName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            if (appliedOverlayFilters.Length > 0)
            {
                return string.Join(" · ", appliedOverlayFilters);
            }

            if (HasMaskEnabled(configuration))
            {
                return "Aperture mask";
            }

            return "Disabled";
        }
    }

    private string ExposureSummary
    {
        get
        {
            var exposure = _status?.LastExposure;
            if (exposure is null)
            {
                return "No exposure telemetry";
            }

            var parts = new List<string>
            {
                FormattableString.Invariant($"{exposure.ExposureMilliseconds} ms"),
                FormattableString.Invariant($"Gain {exposure.Gain}")
            };

            if (exposure.AutoExposure)
            {
                parts.Add("Auto-exposure");
            }

            if (exposure.AutoGain)
            {
                parts.Add("Auto-gain");
            }

            return string.Join(" · ", parts);
        }
    }

    private string LastFrameTimestampText =>
        FormatLocalTimestamp(_status?.LastFrameTimestamp, "Awaiting capture");

    private string TotalIntegrationSummary
    {
        get
        {
            var processed = ProcessedFrameSummary;
            if (processed is null)
            {
                return "Awaiting integration";
            }

            var integrationText = FormatIntegrationText(processed.IntegrationMilliseconds);
            var framesText = processed.FramesStacked == 1
                ? "1 frame"
                : FormattableString.Invariant($"{processed.FramesStacked} frames");

            return FormattableString.Invariant($"{integrationText} · {framesText}");
        }
    }

    private string PipelineProcessingSummary
    {
        get
        {
            var processed = ProcessedFrameSummary;
            if (processed is null)
            {
                return "Awaiting pipeline";
            }

            return processed.ProcessingMilliseconds <= 0
                ? "<1 ms"
                : FormattableString.Invariant($"{processed.ProcessingMilliseconds} ms");
        }
    }

    private string StackingSummary
    {
        get
        {
            var configuration = Configuration;
            if (configuration is null)
            {
                return "Not configured";
            }

            if (!configuration.EnableStacking)
            {
                return "Disabled";
            }

            var bufferSeconds = configuration.StackingBufferIntegrationSeconds;
            var bufferText = bufferSeconds > 0
                ? FormattableString.Invariant($"buffer {bufferSeconds}s")
                : "no buffer";

            return FormattableString.Invariant(
                $"{configuration.StackingFrameCount} frames · {bufferText}");
        }
    }

    private string FramesStackedSummary
    {
        get
        {
            var processed = ProcessedFrameSummary;
            if (processed is null)
            {
                return "Awaiting stack";
            }

            return processed.FramesStacked == 1
                ? "Single frame"
                : FormattableString.Invariant($"{processed.FramesStacked} frames");
        }
    }

    private string CameraHardwareCapabilitiesText =>
        CameraSummary?.HardwareCapabilities is { Count: > 0 } hardware
            ? string.Join(", ", hardware)
            : "Not reported";

    private string RigNameText
    {
        get
        {
            var rig = RigSummary;
            if (rig is null)
            {
                return "No rig configured";
            }

            return string.IsNullOrWhiteSpace(rig.Status)
                ? rig.Name
                : FormattableString.Invariant($"{rig.Name} ({rig.Status})");
        }
    }

    private string SensorSummaryText => SensorSummary is { } sensor
        ? FormattableString.Invariant($"{sensor.WidthPx}×{sensor.HeightPx}px · {sensor.PixelSizeMicrons:0.0} μm")
        : "No sensor configured";

    private string LensSummaryText
    {
        get
        {
            var lens = LensSummary;
            if (lens is null)
            {
                return "No optics configured";
            }

            var fovY = lens.FovYDeg.HasValue
                ? FormattableString.Invariant($" · FOV Y {lens.FovYDeg.Value:0.0}°")
                : string.Empty;

            return FormattableString.Invariant(
                $"{lens.Name} · {lens.FocalLengthMm:0.0} mm · FOV X {lens.FovXDeg:0.0}°{fovY}");
        }
    }

    private string PipelineCapabilitiesText =>
        CameraSummary?.Capabilities is { Count: > 0 } capabilities
            ? string.Join(", ", capabilities)
            : "Not reported";

    private string BackgroundWorkerSummary
    {
        get
        {
            var stacker = BackgroundStacker;
            if (stacker is null)
            {
                return "No data";
            }

            if (!stacker.Enabled)
            {
                return "Disabled";
            }

            return FormattableString.Invariant(
                $"Enabled · Depth {stacker.QueueDepth:N0}/{stacker.QueueCapacity:N0}");
        }
    }

    private string BackgroundQueueDepthSummary => BackgroundStacker is { } stacker
        ? FormattableString.Invariant($"{stacker.QueueDepth:N0}/{stacker.QueueCapacity:N0} frames")
        : "—";

    private string BackgroundQueuePeakSummary => BackgroundStacker is { } stacker
        ? FormattableString.Invariant($"{stacker.PeakQueueDepth:N0} frames")
        : "—";

    private string BackgroundQueueMemorySummary => BackgroundStacker is { } stacker
        ? FormattableString.Invariant($"{stacker.QueueMemoryMegabytes:0.0} MB")
        : "—";

    private string BackgroundQueueLatencySummary =>
        FormatMilliseconds(BackgroundStacker?.LastQueueLatencyMilliseconds);

    private string BackgroundProcessingAverageSummary => BackgroundStacker is { } stacker
        ? FormattableString.Invariant(
            $"Stack {FormatMilliseconds(stacker.AverageStackMilliseconds)} · Filter {FormatMilliseconds(stacker.AverageFilterMilliseconds)}")
        : "—";

    private string BackgroundDropSummary => BackgroundStacker is { } stacker
        ? FormattableString.Invariant($"{stacker.DroppedFrameCount:N0} frames")
        : "—";

    private string BackgroundLastCompletedSummary =>
        FormatLocalTimestamp(BackgroundStacker?.LastCompletedAt);

    private string CapturePacingSummary
    {
        get
        {
            var pacing = CapturePacing;
            if (pacing is null)
            {
                return "Disabled";
            }

            if (!pacing.Enabled)
            {
                return "Disabled";
            }

            return FormattableString.Invariant(
                $"Delay {pacing.AdjustedDelayMilliseconds} ms (base {pacing.BaseDelayMilliseconds} ms)");
        }
    }

    private string CapturePacingPressureSummary => CapturePacing is { } pacing
        ? DescribeQueuePressure(pacing.QueuePressureLevel)
        : "—";

    private string CapturePacingPenaltySummary
    {
        get
        {
            var pacing = CapturePacing;
            if (pacing is null || !pacing.PenaltyActive)
            {
                return "No penalty";
            }

            var expires = FormatLocalTimestamp(pacing.PenaltyExpiresAt);
            return FormattableString.Invariant(
                $"+{pacing.PenaltyAdditionalDelayMilliseconds} ms until {expires}");
        }
    }

    private string CapturePacingUpdatedSummary =>
        FormatLocalTimestamp(CapturePacing?.Timestamp);

    private string ProcessingQueueSummary => ProcessingQueue is { } queue
        ? FormattableString.Invariant($"{queue.Depth:N0}/{queue.Capacity:N0} items")
        : "Disabled";

    private string ProcessingQueueBackpressureSummary => ProcessingQueue is { } queue
        ? FormattableString.Invariant($"{queue.BackpressureEvents:N0} events")
        : "—";

    private string ProcessingQueueProcessingSummary =>
        FormatMilliseconds(ProcessingQueue?.AverageProcessingMilliseconds);

    private string ProcessingQueueUpdatedSummary =>
        FormatLocalTimestamp(ProcessingQueue?.Timestamp);

    private IReadOnlyList<string> AppliedFilters
    {
        get
        {
            if (ProcessedFrameSummary?.AppliedFilters is { Count: > 0 } frameFilters)
            {
                return frameFilters;
            }

            if (Configuration?.FrameFilters is { Count: > 0 } configFilters)
            {
                return configFilters;
            }

            return Array.Empty<string>();
        }
    }

    private string PipelineFiltersSummary => AppliedFilters.Count == 0
        ? "No filters applied"
        : string.Join(", ", AppliedFilters);

    private string DayExposureProfileExposureSummary =>
        FormatExposureLine(ExposureProfiles?.Day);

    private string DayExposureProfileGainSummary =>
        FormatGainLine(ExposureProfiles?.Day);

    private string NightExposureProfileExposureSummary =>
        FormatExposureLine(ExposureProfiles?.Night);

    private string NightExposureProfileGainSummary =>
        FormatGainLine(ExposureProfiles?.Night);

    private string ExposureAnalysisTimestampText =>
        FormatLocalTimestamp(ExposureAnalysis?.Timestamp);

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

            var culture = CultureInfo.CurrentCulture;
            var sampleText = analysis.SampleCount > 0
                ? analysis.SampleCount.ToString("N0", culture)
                : "0";

            return string.Format(
                culture,
                "Avg {0:0.0} · Min {1:0.0} · Max {2:0.0} ({3} samples)",
                analysis.AverageLuminance,
                analysis.MinimumLuminance,
                analysis.MaximumLuminance,
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

            if (!exposureMs.HasValue && !gain.HasValue)
            {
                return "Maintain current settings";
            }

            if (exposureMs.HasValue && gain.HasValue)
            {
                return string.Format(
                    CultureInfo.CurrentCulture,
                    "Adjust to {0} ms · Gain {1}",
                    exposureMs.Value,
                    gain.Value);
            }

            if (exposureMs.HasValue)
            {
                return string.Format(
                    CultureInfo.CurrentCulture,
                    "Adjust exposure to {0} ms",
                    exposureMs.Value);
            }

            return string.Format(
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

    private string DayExposureOverrideSummary =>
        FormatOverrideSummary(DayExposureOverride, "Day");

    private string NightExposureOverrideSummary =>
        FormatOverrideSummary(NightExposureOverride, "Night");

    private string RemoteDispatchModeSummary => RemoteDispatchStatusData is null
        ? "Disabled"
        : RemoteDispatchStatusData.Mode;

    private string RemoteDispatchOutcomeSummary => RemoteDispatchStatusData is null
        ? "—"
        : RemoteDispatchStatusData.Outcome.ToString();

    private string RemoteDispatchSuccessRateSummary => RemoteDispatchStatusData?.Metrics is { } metrics
        ? string.Format(CultureInfo.CurrentCulture, "{0:0.0}%", metrics.SuccessRatePercent)
        : "—";

    private string RemoteDispatchAttemptsSummary => RemoteDispatchStatusData?.Metrics is { } metrics
        ? FormattableString.Invariant($"{metrics.SampleCount:N0} attempts")
        : "—";

    private string RemoteDispatchLatencySummary =>
        FormatMilliseconds(RemoteDispatchStatusData?.Metrics?.AverageLatencyMilliseconds);

    private string RemoteDispatchLastLatencySummary =>
        FormatMilliseconds(RemoteDispatchStatusData?.Metrics?.LastLatencyMilliseconds);

    private string RemoteDispatchCapturedSummary =>
        FormatLocalTimestamp(RemoteDispatchStatusData?.CapturedAtLocal);

    private string RemoteDispatchLastPayloadSummary
    {
        get
        {
            var metrics = RemoteDispatchStatusData?.Metrics;
            if (metrics is null)
            {
                return "—";
            }

            var sizeText = FormatBytes(metrics.LastPayloadBytes);
            var typeParts = new List<string>();

            if (!string.IsNullOrWhiteSpace(metrics.LastPayloadContentType))
            {
                typeParts.Add(metrics.LastPayloadContentType);
            }

            if (!string.IsNullOrWhiteSpace(metrics.LastPayloadExtension))
            {
                typeParts.Add(metrics.LastPayloadExtension);
            }

            return typeParts.Count > 0
                ? FormattableString.Invariant($"{sizeText} · {string.Join(" · ", typeParts)}")
                : sizeText;
        }
    }

    private string RemoteDispatchFormatsSummary
    {
        get
        {
            var formats = RemoteDispatchStatusData?.Metrics?.FormatCounts;
            if (formats is not { Count: > 0 })
            {
                return string.Empty;
            }

            return string.Join(", ", formats.Select(static f =>
                FormattableString.Invariant($"{f.FormatKey} ({f.Count})")));
        }
    }

    private string RemoteDispatchTelemetryUpdatedSummary =>
        FormatLocalTimestamp(RemoteDispatchStatusData?.Timestamp);

    private string RemoteDispatchMessage => RemoteDispatchStatusData?.Message ?? string.Empty;

    private string RemoteDispatchError => RemoteDispatchStatusData?.ErrorMessage ?? string.Empty;

    private string? ProcessedImageSource => _processedImageSource;

    private string? RawImageDisplaySource => _rawImageDisplaySource;

    private string RawImageDownloadLabel => "Download image (PNG)";

    private string RawImageDownloadFileName
    {
        get
        {
            var timestamp = _status?.LastFrameTimestamp ?? ObservatoryClock.UtcNow;
            return FormattableString.Invariant($"raw-frame-{timestamp:yyyyMMdd-HHmmss}.png");
        }
    }

    private string? RawImageDownloadSource => _rawImageDisplaySource;

    private string ConfigurationVersion => _configurationVersion == 0
        ? "Unknown"
        : _configurationVersion.ToString(CultureInfo.InvariantCulture);

    private string? BuildFrameDetailUrl(string route, Guid? frameId, DateTimeOffset? timestamp, string? type = null)
    {
        if (frameId is null || timestamp is null)
        {
            return null;
        }

        var builder = new StringBuilder(FormattableString.Invariant(
            $"/monitor/{route}?frameId={Uri.EscapeDataString(frameId.Value.ToString("D", CultureInfo.InvariantCulture))}&datetime={Uri.EscapeDataString(timestamp.Value.ToString("O", CultureInfo.InvariantCulture))}"));

        if (!string.IsNullOrWhiteSpace(type))
        {
            builder.Append("&type=").Append(Uri.EscapeDataString(type));
        }

        return builder.ToString();
    }

    private string FormatLocalTimestamp(DateTimeOffset? timestamp, string whenMissing = "—")
        => timestamp.HasValue
            ? ObservatoryClock.ToLocal(timestamp.Value).ToString("MMM d, yyyy • h:mm:ss tt", CultureInfo.CurrentCulture)
            : whenMissing;

    private static string FormatMilliseconds(double? value)
        => value.HasValue ? FormattableString.Invariant($"{value.Value:0.0} ms") : "—";

    private static string FormatMilliseconds(long? value)
        => value.HasValue ? FormattableString.Invariant($"{value.Value:0} ms") : "—";

    private static string FormatBytes(long? bytes)
    {
        if (!bytes.HasValue)
        {
            return "—";
        }

        var size = bytes.Value;
        if (size < 1024)
        {
            return FormattableString.Invariant($"{size} B");
        }

        var kilobytes = size / 1024d;
        if (kilobytes < 1024)
        {
            return FormattableString.Invariant($"{kilobytes:0.0} KB");
        }

        var megabytes = kilobytes / 1024d;
        if (megabytes < 1024)
        {
            return FormattableString.Invariant($"{megabytes:0.0} MB");
        }

        var gigabytes = megabytes / 1024d;
        return FormattableString.Invariant($"{gigabytes:0.00} GB");
    }

    private static string DescribeQueuePressure(int level) => level switch
    {
        <= 0 => "Normal",
        1 => "Elevated",
        2 => "High",
        3 => "Critical",
        _ => FormattableString.Invariant($"Level {level}")
    };

    private string FormatOverrideSummary(ExposureOverrideSnapshot? snapshot, string label)
    {
        if (snapshot is null)
        {
            return FormattableString.Invariant($"{label} override not applied");
        }

        var parts = new List<string>
        {
            FormattableString.Invariant($"Target {snapshot.TargetExposureMilliseconds} ms · Gain {snapshot.TargetGain}")
        };

        if (snapshot.AppliedExposureMilliseconds != snapshot.TargetExposureMilliseconds
            || snapshot.AppliedGain != snapshot.TargetGain)
        {
            parts.Add(FormattableString.Invariant(
                $"Applied {snapshot.AppliedExposureMilliseconds} ms · Gain {snapshot.AppliedGain}"));
        }

        if (snapshot.ExpiresAt is { } expiresAt)
        {
            var localExpiry = ObservatoryClock.ToLocal(expiresAt);
            parts.Add(FormattableString.Invariant($"expires {localExpiry:MMM d h:mm tt}"));
        }

        return string.Join(" · ", parts);
    }

    private static bool HasOverlaysEnabled(CameraConfiguration configuration) =>
        configuration.EnableImageOverlays
        || HasMaskEnabled(configuration)
        || configuration.FrameFilters.Any(IsOverlayFilterName);

    private static bool HasMaskEnabled(CameraConfiguration configuration) =>
        configuration.EnableCircularApertureMask
        || configuration.FrameFilters.Any(static filter =>
            string.Equals(filter, FrameFilterNames.CircularApertureMask, StringComparison.OrdinalIgnoreCase));

    private static bool IsOverlayFilterName(string filterName) =>
        string.Equals(filterName, FrameFilterNames.CardinalDirections, StringComparison.OrdinalIgnoreCase)
        || string.Equals(filterName, FrameFilterNames.ConstellationFigures, StringComparison.OrdinalIgnoreCase)
        || string.Equals(filterName, FrameFilterNames.CelestialAnnotations, StringComparison.OrdinalIgnoreCase)
        || string.Equals(filterName, FrameFilterNames.OverlayText, StringComparison.OrdinalIgnoreCase)
        || string.Equals(filterName, FrameFilterNames.DiagnosticsOverlay, StringComparison.OrdinalIgnoreCase);

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

        var seconds = integrationMilliseconds / 1_000d;
        if (seconds < 60)
        {
            return FormattableString.Invariant($"{seconds:0.0} s");
        }

        var minutes = seconds / 60d;
        if (minutes < 60)
        {
            return FormattableString.Invariant($"{minutes:0.0} min");
        }

        var hours = minutes / 60d;
        return FormattableString.Invariant($"{hours:0.0} hr");
    }

    private static string FormatExposureLine(ExposureProfileBucketSummary? bucket)
    {
        if (bucket is null)
        {
            return "Exposure configuration unavailable";
        }

        var normalized = bucket.Normalize();
        var culture = CultureInfo.CurrentCulture;

        var rangeText = normalized.MinExposureMilliseconds == normalized.MaxExposureMilliseconds
            ? string.Format(culture, "fixed {0} ms", normalized.MinExposureMilliseconds)
            : string.Format(culture, "range {0}-{1} ms", normalized.MinExposureMilliseconds, normalized.MaxExposureMilliseconds);

        if (normalized.StartExposureMilliseconds != normalized.BaselineExposureMilliseconds)
        {
            return string.Format(
                culture,
                "Exposure start {0} ms · default {1} ms · {2}",
                normalized.StartExposureMilliseconds,
                normalized.BaselineExposureMilliseconds,
                rangeText);
        }

        return string.Format(
            culture,
            "Exposure default {0} ms · {1}",
            normalized.BaselineExposureMilliseconds,
            rangeText);
    }

    private static string FormatGainLine(ExposureProfileBucketSummary? bucket)
    {
        if (bucket is null)
        {
            return "Gain configuration unavailable";
        }

        var normalized = bucket.Normalize();
        var culture = CultureInfo.CurrentCulture;

        var rangeText = normalized.MinGain == normalized.MaxGain
            ? string.Format(culture, "fixed {0}", normalized.MinGain)
            : string.Format(culture, "range {0}-{1}", normalized.MinGain, normalized.MaxGain);

        if (normalized.StartGain != normalized.BaselineGain)
        {
            return string.Format(
                culture,
                "Gain start {0} · default {1} · {2}",
                normalized.StartGain,
                normalized.BaselineGain,
                rangeText);
        }

        return string.Format(
            culture,
            "Gain default {0} · {1}",
            normalized.BaselineGain,
            rangeText);
    }
}
