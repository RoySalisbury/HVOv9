using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using HVO.SkyMonitorV5.RPi.Cameras.Optics;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Exports;
using HVO.SkyMonitorV5.RPi.Pipeline.Composition;
using HVO.SkyMonitorV5.RPi.Skia;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading;
using HVO.SkyMonitorV5.RPi.Telemetry;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Storage;

public sealed class FrameStateStore : IFrameStateStore, IDisposable
{
    private readonly object _sync = new();
    private readonly ILogger<FrameStateStore>? _logger;
    private readonly IDisposable? _optionsReloadSubscription;
    private readonly IObservatoryClock _clock;
    private readonly ISkyMonitorTelemetryRecorder? _telemetryRecorder;

    private CameraConfiguration _configuration;
    private int _configurationVersion;
    private ProcessedFrame? _latestProcessedFrame;
    private RawFrameSnapshot? _latestRawFrame;
    private DateTimeOffset? _lastFrameTimestamp;
    private ExposureAnalysisResult? _latestExposureAnalysis;
    private DateTimeOffset? _latestExposureAnalysisTimestamp;
    private bool _isRunning;
    private Exception? _lastError;
    private CameraDescriptor? _cameraDescriptor;
    private RigSpec? _rigSpec;
    private ExposureProfileSummary? _exposureProfiles;
    private BackgroundStackerStatus? _backgroundStackerStatus;
    private CapturePacingStatus? _capturePacingStatus;
    private ProcessingQueueStatus? _processingQueueStatus;
    private ExposureOverrideState? _dayOverride;
    private ExposureOverrideState? _nightOverride;
    private RemoteDispatchStatus? _remoteDispatchStatus;
    private RemoteDispatchMetricsSnapshot? _remoteDispatchMetrics;
    private readonly Queue<RemoteDispatchHistorySample> _remoteDispatchHistory = new();
    private const int RemoteDispatchHistoryCapacity = 480;
    private readonly Queue<BackgroundStackerHistorySample> _backgroundStackerHistory = new();
    private const int BackgroundStackerHistoryCapacity = 720;
    private readonly ComposedFrameQueue _composedFrames;
    private const int ComposedFrameHistoryCapacity = 32;
    private static readonly JsonSerializerOptions TelemetryEventSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public FrameStateStore(
        IOptionsMonitor<CameraPipelineOptions> optionsMonitor,
        IObservatoryClock clock,
        ILogger<FrameStateStore>? logger = null,
        ISkyMonitorTelemetryRecorder? telemetryRecorder = null)
    {
        if (optionsMonitor is null)
        {
            throw new ArgumentNullException(nameof(optionsMonitor));
        }

        _logger = logger;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _configuration = CameraConfiguration.FromOptions(optionsMonitor.CurrentValue);
        _telemetryRecorder = telemetryRecorder;
        UpdateExposureProfiles(optionsMonitor.CurrentValue);
        _composedFrames = new ComposedFrameQueue(ComposedFrameHistoryCapacity);

        _optionsReloadSubscription = optionsMonitor.OnChange(OnPipelineOptionsChanged);
    }

    public CameraConfiguration Configuration
    {
        get
        {
            lock (_sync)
            {
                return _configuration;
            }
        }
    }

    public int ConfigurationVersion => Volatile.Read(ref _configurationVersion);

    public CameraDescriptor? CameraDescriptor
    {
        get
        {
            lock (_sync)
            {
                return _cameraDescriptor;
            }
        }
    }

    public RigSpec? Rig
    {
        get
        {
            lock (_sync)
            {
                return _rigSpec;
            }
        }
    }

    public ProcessedFrame? LatestProcessedFrame
    {
        get
        {
            lock (_sync)
            {
                return _latestProcessedFrame;
            }
        }
    }

    public RawFrameSnapshot? LatestRawFrame
    {
        get
        {
            lock (_sync)
            {
                return _latestRawFrame;
            }
        }
    }

    public DateTimeOffset? LastFrameTimestamp
    {
        get
        {
            lock (_sync)
            {
                return _lastFrameTimestamp;
            }
        }
    }

    public ExposureAnalysisResult? LatestExposureAnalysis
    {
        get
        {
            lock (_sync)
            {
                return _latestExposureAnalysis;
            }
        }
    }

    public DateTimeOffset? LatestExposureAnalysisTimestamp
    {
        get
        {
            lock (_sync)
            {
                return _latestExposureAnalysisTimestamp;
            }
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _isRunning;
            }
        }
    }

    public Exception? LastError
    {
        get
        {
            lock (_sync)
            {
                return _lastError;
            }
        }
    }

    public CapturePacingStatus? CapturePacingStatus
    {
        get
        {
            lock (_sync)
            {
                return _capturePacingStatus;
            }
        }
    }

    public BackgroundStackerStatus? BackgroundStackerStatus
    {
        get
        {
            lock (_sync)
            {
                return _backgroundStackerStatus;
            }
        }
    }

    public ProcessingQueueStatus? ProcessingQueueStatus
    {
        get
        {
            lock (_sync)
            {
                return _processingQueueStatus;
            }
        }
    }

    public RemoteDispatchStatus? RemoteDispatchStatus
    {
        get
        {
            lock (_sync)
            {
                return _remoteDispatchStatus;
            }
        }
    }

    public RemoteDispatchMetricsSnapshot? RemoteDispatchMetrics
    {
        get
        {
            lock (_sync)
            {
                return _remoteDispatchMetrics;
            }
        }
    }

    public ExposureOverrideState? DayExposureOverride
    {
        get
        {
            lock (_sync)
            {
                return _dayOverride;
            }
        }
    }

    public ExposureOverrideState? NightExposureOverride
    {
        get
        {
            lock (_sync)
            {
                return _nightOverride;
            }
        }
    }

    public void UpdateConfiguration(CameraConfiguration configuration)
    {
        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        if (TryUpdateConfiguration(configuration, force: true, out var newVersion))
        {
            _logger?.LogInformation(
                "Camera configuration updated via API. Configuration version is now {ConfigurationVersion}.",
                newVersion);
        }
    }

    public void UpdateFrame(RawFrameSnapshot rawFrame, ProcessedFrame processedFrame)
    {
        SKImage? historyImage = SkiaImageUtilities.CloneToRaster(processedFrame.ImmutableImage);

        lock (_sync)
        {
            if (_latestRawFrame is not null && !ReferenceEquals(_latestRawFrame, rawFrame))
            {
                _latestRawFrame.Image.Dispose();
                _latestRawFrame.ImmutableImage?.Dispose();
            }

            if (_latestProcessedFrame is not null && !ReferenceEquals(_latestProcessedFrame, processedFrame))
            {
                _latestProcessedFrame.ImmutableImage?.Dispose();
            }
            var localizedRaw = rawFrame with { Timestamp = _clock.ToLocal(rawFrame.Timestamp) };
            var localizedProcessed = processedFrame with { Timestamp = _clock.ToLocal(processedFrame.Timestamp) };

            _latestRawFrame = localizedRaw;
            _latestProcessedFrame = localizedProcessed;
            _lastFrameTimestamp = localizedProcessed.Timestamp;

            if (historyImage is SKImage imageForHistory)
            {
                var composedEntry = new ComposedFrame(
                    localizedProcessed.FrameId,
                    localizedProcessed.Timestamp,
                    imageForHistory,
                    localizedProcessed.FramesStacked,
                    localizedProcessed.IntegrationMilliseconds,
                    localizedProcessed.AppliedFilters,
                    localizedProcessed.FilterExecutions,
                    localizedProcessed.SurfaceMilliseconds);

                _composedFrames.Enqueue(composedEntry);
                historyImage = null;
            }
        }

        historyImage?.Dispose();
    }

    public void UpdateRunningState(bool isRunning)
    {
        lock (_sync)
        {
            _isRunning = isRunning;
        }
    }

    public void UpdateRig(RigSpec rig)
    {
        lock (_sync)
        {
            _rigSpec = rig;
            _cameraDescriptor = rig.Descriptor ?? _cameraDescriptor;
        }
    }

    public void SetLastError(Exception? exception)
    {
        lock (_sync)
        {
            _lastError = exception;
        }
    }

    public void UpdateExposureAnalysis(ExposureAnalysisResult analysis, DateTimeOffset capturedAtUtc)
    {
        if (analysis is null)
        {
            throw new ArgumentNullException(nameof(analysis));
        }

        var localizedTimestamp = _clock.ToLocal(capturedAtUtc);

        lock (_sync)
        {
            _latestExposureAnalysis = analysis;
            _latestExposureAnalysisTimestamp = localizedTimestamp;
        }
    }

    public void UpdateBackgroundStackerStatus(BackgroundStackerStatus status)
    {
        if (status is null)
        {
            throw new ArgumentNullException(nameof(status));
        }

    BackgroundStackerHistorySample sample = null!;

        lock (_sync)
        {
            var localizedStatus = status with
            {
                LastEnqueuedAt = status.LastEnqueuedAt is { } enqueued ? _clock.ToLocal(enqueued) : null,
                LastCompletedAt = status.LastCompletedAt is { } completed ? _clock.ToLocal(completed) : null
            };

            _backgroundStackerStatus = localizedStatus;
            sample = EnqueueBackgroundStackerSample(localizedStatus);
        }

        _telemetryRecorder?.RecordBackgroundStackerSample(_clock.UtcNow, sample);
    }

    public void UpdateCapturePacingStatus(CapturePacingStatus status)
    {
        if (status is null)
        {
            throw new ArgumentNullException(nameof(status));
        }

    CapturePacingStatus localizedStatus = null!;

        lock (_sync)
        {
            var localizedTimestamp = _clock.ToLocal(status.Timestamp);
            DateTimeOffset? penaltyExpires = status.PenaltyExpiresAt is { } expires
                ? _clock.ToLocal(expires)
                : null;

            localizedStatus = status with
            {
                Timestamp = localizedTimestamp,
                PenaltyExpiresAt = penaltyExpires
            };

            _capturePacingStatus = localizedStatus;
        }

        _telemetryRecorder?.RecordCapturePacingSample(status.Timestamp, localizedStatus);
    }

    public void UpdateProcessingQueueStatus(ProcessingQueueStatus status)
    {
    ProcessingQueueStatus localizedStatus = null!;

        lock (_sync)
        {
            var localizedTimestamp = _clock.ToLocal(status.Timestamp);
            localizedStatus = status with
            {
                Timestamp = localizedTimestamp
            };

            _processingQueueStatus = localizedStatus;
        }

        _telemetryRecorder?.RecordProcessingQueueSample(status.Timestamp, localizedStatus);
    }

    public void UpdateRemoteDispatchStatus(RemoteDispatchStatus status, RemoteDispatchEventMetrics eventMetrics)
    {
        if (status is null)
        {
            throw new ArgumentNullException(nameof(status));
        }

        var latency = SanitizeLatency(eventMetrics.LatencyMilliseconds);
        var contentType = NormalizeContentType(eventMetrics.PayloadContentType);
        var extension = NormalizeExtension(eventMetrics.PayloadFileExtension);

    RemoteDispatchHistorySample sample = null!;
    string? formatKey = null;

        lock (_sync)
        {
            var localizedTimestamp = _clock.ToLocal(status.Timestamp);
            sample = new RemoteDispatchHistorySample(
                Timestamp: localizedTimestamp,
                Outcome: status.Outcome,
                Mode: status.Mode,
                LatencyMilliseconds: latency,
                PayloadBytes: eventMetrics.PayloadBytes,
                PayloadContentType: contentType,
                PayloadExtension: extension,
                Message: status.Message,
                ErrorMessage: status.ErrorMessage);

            EnqueueRemoteDispatchSample(sample);

            var snapshot = ComputeRemoteDispatchMetricsSnapshot(_clock.LocalNow);
            _remoteDispatchMetrics = snapshot;

            _remoteDispatchStatus = status with
            {
                Timestamp = localizedTimestamp,
                Metrics = snapshot
            };

            formatKey = BuildFormatKey(sample);
        }

        _telemetryRecorder?.RecordRemoteDispatchAttempt(
            status.Timestamp,
            sample.Timestamp,
            status.Mode,
            status.Outcome,
            sample.LatencyMilliseconds,
            eventMetrics.PayloadBytes,
            sample.PayloadContentType,
            sample.PayloadExtension,
            status.Message,
            status.ErrorMessage,
            formatKey);

        var severity = ResolveRemoteDispatchSeverity(status.Outcome);
        var summary = ResolveRemoteDispatchSummary(status.Outcome, status.Message, status.ErrorMessage);
        var properties = new RemoteDispatchTelemetryEventProperties(
            status.Mode,
            status.Outcome.ToString(),
            sample.LatencyMilliseconds,
            eventMetrics.PayloadBytes,
            sample.PayloadContentType,
            sample.PayloadExtension,
            formatKey,
            sample.Timestamp,
            status.Message,
            status.ErrorMessage);

        var propertiesJson = JsonSerializer.Serialize(properties, TelemetryEventSerializerOptions);

        _telemetryRecorder?.RecordTelemetryEvent(
            status.Timestamp,
            sample.Timestamp,
            category: "RemoteDispatch",
            eventType: $"RemoteDispatch{status.Outcome}",
            severity: severity,
            summary: summary,
            detail: status.ErrorMessage,
            propertiesJson: propertiesJson);
    }

    private static string ResolveRemoteDispatchSeverity(RemoteDispatchOutcome outcome)
        => outcome switch
        {
            RemoteDispatchOutcome.Succeeded => "Information",
            RemoteDispatchOutcome.Disabled => "Debug",
            RemoteDispatchOutcome.Skipped => "Debug",
            RemoteDispatchOutcome.Failed => "Error",
            _ => "Information"
        };

    private static string ResolveRemoteDispatchSummary(RemoteDispatchOutcome outcome, string? message, string? errorMessage)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            return message!;
        }

        if (outcome == RemoteDispatchOutcome.Failed && !string.IsNullOrWhiteSpace(errorMessage))
        {
            return errorMessage!;
        }

        return outcome switch
        {
            RemoteDispatchOutcome.Succeeded => "Remote dispatch succeeded.",
            RemoteDispatchOutcome.Disabled => "Remote dispatch disabled.",
            RemoteDispatchOutcome.Skipped => "Remote dispatch skipped.",
            RemoteDispatchOutcome.Failed => "Remote dispatch failed.",
            _ => "Remote dispatch update."
        };
    }

    private sealed record RemoteDispatchTelemetryEventProperties(
        string Mode,
        string Outcome,
        double? LatencyMilliseconds,
        long? PayloadBytes,
        string? PayloadContentType,
        string? PayloadExtension,
        string? FormatKey,
        DateTimeOffset CapturedAtLocal,
        string? Message,
        string? ErrorMessage);

    public void UpdateExposureOverride(ExposureOverrideUpdate update)
    {
        if (update is null)
        {
            throw new ArgumentNullException(nameof(update));
        }

        var timestampLocal = _clock.ToLocal(update.Timestamp);
        var expiresUtc = update.Timestamp + update.TimeToLive;
        var expiresLocal = _clock.ToLocal(expiresUtc);

        var state = new ExposureOverrideState(
            update.Bucket,
            update.Baseline,
            update.Target,
            update.Applied,
            timestampLocal,
            expiresLocal);

        lock (_sync)
        {
            if (update.Bucket == ExposureOverrideBucket.Day)
            {
                _dayOverride = state;
            }
            else
            {
                _nightOverride = state;
            }
        }
    }

    public IReadOnlyList<BackgroundStackerHistorySample> GetBackgroundStackerHistory()
    {
        lock (_sync)
        {
            if (_backgroundStackerHistory.Count == 0)
            {
                return Array.Empty<BackgroundStackerHistorySample>();
            }

            return _backgroundStackerHistory.ToArray();
        }
    }

    public IReadOnlyList<RemoteDispatchHistorySample> GetRemoteDispatchHistory()
    {
        lock (_sync)
        {
            if (_remoteDispatchHistory.Count == 0)
            {
                return Array.Empty<RemoteDispatchHistorySample>();
            }

            return _remoteDispatchHistory.ToArray();
        }
    }

    public IReadOnlyList<ComposedFrame> GetComposedFrameHistory()
        => _composedFrames.Snapshot();

    public void Dispose()
    {
        lock (_sync)
        {
            if (_latestRawFrame is not null)
            {
                _latestRawFrame.Image.Dispose();
                _latestRawFrame.ImmutableImage?.Dispose();
                _latestRawFrame = null;
            }

            if (_latestProcessedFrame is not null)
            {
                _latestProcessedFrame.ImmutableImage?.Dispose();
                _latestProcessedFrame = null;
            }
        }

        _optionsReloadSubscription?.Dispose();
        _composedFrames.Dispose();
    }

    public AllSkyStatusResponse GetStatus()
    {
        lock (_sync)
        {
            var descriptor = _cameraDescriptor ?? new CameraDescriptor(
                Manufacturer: "Unknown",
                Model: "Unknown",
                DriverVersion: "Unknown",
                AdapterName: "Unknown",
                Capabilities: Array.Empty<string>());

            var processedSummary = CreateProcessedSummary(_latestProcessedFrame);
            var rawSummary = CreateRawSummary(_latestRawFrame);
            var exposure = _latestRawFrame?.Exposure;
            var rigSpec = _rigSpec;
            var rig = CreateRigSummary(rigSpec);
            var cameraSummary = CreateCameraSummary(descriptor, rigSpec, exposure, _isRunning, _lastError);
            var analysis = CreateExposureAnalysisSummary(_latestExposureAnalysisTimestamp, _latestExposureAnalysis);
            var overrides = CreateExposureOverrideSummary(_dayOverride, _nightOverride);
            var remoteDispatch = _remoteDispatchStatus;
            var exposureProfiles = _exposureProfiles;

            var summary = new AllSkyStatusSummary(
                Camera: cameraSummary,
                Rig: rig,
                Configuration: _configuration,
                ProcessedFrame: processedSummary,
                RawFrame: rawSummary,
                BackgroundStacker: _backgroundStackerStatus,
                CapturePacing: _capturePacingStatus,
                ProcessingQueue: _processingQueueStatus,
                ExposureProfiles: exposureProfiles,
                ExposureAnalysis: analysis,
                ExposureOverrides: overrides,
                RemoteDispatch: remoteDispatch);

            return new AllSkyStatusResponse(
                IsRunning: _isRunning,
                LastFrameTimestamp: _lastFrameTimestamp,
                LastExposure: exposure,
                Configuration: _configuration,
                ProcessedFrame: processedSummary,
                RawFrame: rawSummary,
                BackgroundStacker: _backgroundStackerStatus,
                CapturePacing: _capturePacingStatus,
                ProcessingQueue: _processingQueueStatus,
                Camera: descriptor,
                Rig: rigSpec,
                Summary: summary,
                ExposureProfiles: exposureProfiles,
                ExposureAnalysis: analysis,
                ExposureOverrides: overrides,
                RemoteDispatch: remoteDispatch);
        }
    }

    private ExposureAnalysisSummary? CreateExposureAnalysisSummary(DateTimeOffset? timestamp, ExposureAnalysisResult? analysis)
    {
        if (timestamp is null || analysis is null)
        {
            return null;
        }

        var metrics = analysis.Metrics;
        return new ExposureAnalysisSummary(
            Timestamp: timestamp,
            LightingCondition: analysis.LightingCondition,
            AverageLuminance: metrics.AverageLuminance,
            MinimumLuminance: metrics.MinimumLuminance,
            MaximumLuminance: metrics.MaximumLuminance,
            SampleCount: metrics.SampleCount,
            SuggestedExposureMilliseconds: analysis.SuggestedExposure?.ExposureMilliseconds,
            SuggestedGain: analysis.SuggestedExposure?.Gain,
            Notes: analysis.Notes);
    }

    private ExposureOverrideSummary? CreateExposureOverrideSummary(ExposureOverrideState? day, ExposureOverrideState? night)
    {
        var daySnapshot = CreateOverrideSnapshot(day);
        var nightSnapshot = CreateOverrideSnapshot(night);

        if (daySnapshot is null && nightSnapshot is null)
        {
            return null;
        }

        return new ExposureOverrideSummary(daySnapshot, nightSnapshot);
    }

    private ExposureOverrideSnapshot? CreateOverrideSnapshot(ExposureOverrideState? state)
    {
        if (state is null)
        {
            return null;
        }

        if (state.ExpiresAt <= _clock.LocalNow)
        {
            if (state.Bucket == ExposureOverrideBucket.Day)
            {
                _dayOverride = null;
            }
            else
            {
                _nightOverride = null;
            }

            return null;
        }

        return new ExposureOverrideSnapshot(
            state.Bucket,
            state.Timestamp,
            state.ExpiresAt,
            state.Baseline.ExposureMilliseconds,
            state.Baseline.Gain,
            state.Target.ExposureMilliseconds,
            state.Target.Gain,
            state.Applied.ExposureMilliseconds,
            state.Applied.Gain);
    }

    private BackgroundStackerHistorySample EnqueueBackgroundStackerSample(BackgroundStackerStatus status)
    {
        var sample = new BackgroundStackerHistorySample(
            Timestamp: _clock.LocalNow,
            QueueFillPercentage: status.QueueFillPercentage,
            QueueDepth: status.QueueDepth,
            QueueCapacity: status.QueueCapacity,
            QueueLatencyMilliseconds: status.LastQueueLatencyMilliseconds,
            StackDurationMilliseconds: status.LastStackMilliseconds,
            FilterDurationMilliseconds: status.LastFilterMilliseconds,
            QueuePressureLevel: status.QueuePressureLevel,
            SecondsSinceLastCompleted: status.SecondsSinceLastCompleted,
            QueueMemoryMegabytes: status.QueueMemoryMegabytes);

        if (_backgroundStackerHistory.Count >= BackgroundStackerHistoryCapacity)
        {
            _backgroundStackerHistory.Dequeue();
        }

        _backgroundStackerHistory.Enqueue(sample);

        return sample;
    }

    private void EnqueueRemoteDispatchSample(RemoteDispatchHistorySample sample)
    {
        if (_remoteDispatchHistory.Count >= RemoteDispatchHistoryCapacity)
        {
            _remoteDispatchHistory.Dequeue();
        }

        _remoteDispatchHistory.Enqueue(sample);
    }

    private RemoteDispatchMetricsSnapshot ComputeRemoteDispatchMetricsSnapshot(DateTimeOffset generatedAt)
    {
        if (_remoteDispatchHistory.Count == 0)
        {
            return new RemoteDispatchMetricsSnapshot(
                GeneratedAt: generatedAt,
                SampleCount: 0,
                SuccessCount: 0,
                FailureCount: 0,
                SkippedCount: 0,
                SuccessRatePercent: 0,
                AverageLatencyMilliseconds: null,
                PeakLatencyMilliseconds: null,
                LastLatencyMilliseconds: null,
                LastPayloadBytes: null,
                LastPayloadContentType: null,
                LastPayloadExtension: null,
                FormatCounts: Array.Empty<RemoteDispatchFormatSummary>());
        }

        var samples = _remoteDispatchHistory.ToArray();
        var successCount = 0;
        var failureCount = 0;
        var skippedCount = 0;
        double latencySum = 0;
        var latencyCount = 0;
        double? peakLatency = null;
    var formatCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var sample in samples)
        {
            switch (sample.Outcome)
            {
                case RemoteDispatchOutcome.Succeeded:
                    successCount++;
                    break;
                case RemoteDispatchOutcome.Failed:
                    failureCount++;
                    break;
                default:
                    skippedCount++;
                    break;
            }

            if (sample.LatencyMilliseconds is { } latency)
            {
                latencySum += latency;
                latencyCount++;
                if (!peakLatency.HasValue || latency > peakLatency.Value)
                {
                    peakLatency = latency;
                }
            }

            var formatKey = BuildFormatKey(sample);
            if (formatKey is not null)
            {
                formatCounts.TryGetValue(formatKey, out var count);
                formatCounts[formatKey] = count + 1;
            }
        }

    double? averageLatency = latencyCount > 0 ? latencySum / latencyCount : null;
        var attemptCount = successCount + failureCount;
        var successRatePercent = attemptCount > 0
            ? successCount / (double)attemptCount * 100d
            : 0d;

        var lastSample = samples[^1];

        RemoteDispatchFormatSummary[] formatSummaries;
        if (formatCounts.Count == 0)
        {
            formatSummaries = Array.Empty<RemoteDispatchFormatSummary>();
        }
        else
        {
            formatSummaries = formatCounts
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => new RemoteDispatchFormatSummary(kv.Key, kv.Value))
                .ToArray();
        }

        return new RemoteDispatchMetricsSnapshot(
            GeneratedAt: generatedAt,
            SampleCount: samples.Length,
            SuccessCount: successCount,
            FailureCount: failureCount,
            SkippedCount: skippedCount,
            SuccessRatePercent: successRatePercent,
            AverageLatencyMilliseconds: SanitizeLatency(averageLatency),
            PeakLatencyMilliseconds: SanitizeLatency(peakLatency),
            LastLatencyMilliseconds: lastSample.LatencyMilliseconds,
            LastPayloadBytes: lastSample.PayloadBytes,
            LastPayloadContentType: lastSample.PayloadContentType,
            LastPayloadExtension: lastSample.PayloadExtension,
            FormatCounts: formatSummaries);
    }

    private static double? SanitizeLatency(double? latency)
    {
        if (!latency.HasValue)
        {
            return null;
        }

        var value = latency.Value;
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return null;
        }

        if (value < 0)
        {
            value = 0d;
        }

        return value;
    }

    private static string? NormalizeContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return null;
        }

        var normalized = contentType.Trim().ToLowerInvariant();
        return normalized.Length == 0 ? null : normalized;
    }

    private static string? NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        var trimmed = extension.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (!trimmed.StartsWith(".", StringComparison.Ordinal))
        {
            trimmed = FormattableString.Invariant($".{trimmed}");
        }

        if (trimmed.Length <= 1)
        {
            return null;
        }

        return trimmed.ToLowerInvariant();
    }

    private static string? BuildFormatKey(RemoteDispatchHistorySample sample)
    {
        if (!string.IsNullOrWhiteSpace(sample.PayloadExtension))
        {
            return sample.PayloadExtension;
        }

        if (!string.IsNullOrWhiteSpace(sample.PayloadContentType))
        {
            return sample.PayloadContentType;
        }

        return null;
    }

    private void OnPipelineOptionsChanged(CameraPipelineOptions options)
    {
        if (options is null)
        {
            return;
        }

        UpdateExposureProfiles(options);
        var updatedConfiguration = CameraConfiguration.FromOptions(options);
        if (TryUpdateConfiguration(updatedConfiguration, force: false, out var newVersion))
        {
            _logger?.LogInformation(
                "Camera pipeline options reloaded from configuration; version advanced to {ConfigurationVersion}.",
                newVersion);
        }
    }

    private void UpdateExposureProfiles(CameraPipelineOptions options)
    {
        if (options is null)
        {
            return;
        }

        var summary = BuildExposureProfileSummary(options);

        lock (_sync)
        {
            _exposureProfiles = summary;
        }
    }

    private static ExposureProfileSummary BuildExposureProfileSummary(CameraPipelineOptions options)
    {
        var day = new ExposureProfileBucketSummary(
            Name: "Day",
            BaselineExposureMilliseconds: options.DayExposureMilliseconds,
            StartExposureMilliseconds: options.DayStartExposureMilliseconds,
            MinExposureMilliseconds: options.DayMinExposureMilliseconds,
            MaxExposureMilliseconds: options.DayMaxExposureMilliseconds,
            BaselineGain: options.DayGain,
            StartGain: options.DayStartGain,
            MinGain: options.DayMinGain,
            MaxGain: options.DayMaxGain).Normalize();

        var night = new ExposureProfileBucketSummary(
            Name: "Night",
            BaselineExposureMilliseconds: options.NightExposureMilliseconds,
            StartExposureMilliseconds: options.NightStartExposureMilliseconds,
            MinExposureMilliseconds: options.NightMinExposureMilliseconds,
            MaxExposureMilliseconds: options.NightMaxExposureMilliseconds,
            BaselineGain: options.NightGain,
            StartGain: options.NightStartGain,
            MinGain: options.NightMinGain,
            MaxGain: options.NightMaxGain).Normalize();

        return new ExposureProfileSummary(day, night);
    }

    private bool TryUpdateConfiguration(CameraConfiguration configuration, bool force, out int newVersion)
    {
        lock (_sync)
        {
            if (!force && _configuration.Equals(configuration))
            {
                newVersion = _configurationVersion;
                return false;
            }

            _configuration = configuration;
            newVersion = ++_configurationVersion;
            return true;
        }
    }

    private static ProcessedFrameSummary? CreateProcessedSummary(ProcessedFrame? frame)
    {
        if (frame is null)
        {
            return null;
        }

        return new ProcessedFrameSummary(
            frame.FramesStacked,
            frame.IntegrationMilliseconds,
            frame.AppliedFilters,
            frame.ProcessingMilliseconds)
        {
            SurfaceMilliseconds = frame.SurfaceMilliseconds,
            FilterExecutions = frame.FilterExecutions
        };
    }

    private static RawFrameSummary? CreateRawSummary(RawFrameSnapshot? frame)
    {
        if (frame is null)
        {
            return null;
        }

        var descriptor = frame.ImageDescriptor;
        var width = descriptor?.Width ?? frame.Image?.Width ?? 0;
        var height = descriptor?.Height ?? frame.Image?.Height ?? 0;
        return new RawFrameSummary(
            Timestamp: frame.Timestamp,
            Width: width,
            Height: height,
            ExposureMilliseconds: frame.Exposure.ExposureMilliseconds,
            Gain: frame.Exposure.Gain,
            ImageDescriptor: descriptor);
    }

    private static AllSkyCameraSummary CreateCameraSummary(
        CameraDescriptor descriptor,
        RigSpec? rig,
        ExposureSettings? exposure,
        bool isRunning,
        Exception? lastError)
    {
        var name = string.IsNullOrWhiteSpace(descriptor.Model)
            ? descriptor.Manufacturer
            : FormattableString.Invariant($"{descriptor.Manufacturer} {descriptor.Model}").Trim();

        var status = lastError is not null
            ? "Error"
            : isRunning
                ? "Capturing"
                : "Idle";

        var pipelineCapabilities = descriptor.Capabilities as IReadOnlyList<string>
            ?? descriptor.Capabilities?.ToArray()
            ?? Array.Empty<string>();

        var hardwareCapabilities = rig?.Capabilities.ToDisplayTags() ?? Array.Empty<string>();

        return new AllSkyCameraSummary(
            Name: string.IsNullOrWhiteSpace(name) ? "Unknown" : name,
            Capabilities: pipelineCapabilities,
            HardwareCapabilities: hardwareCapabilities,
            ExposureMilliseconds: exposure?.ExposureMilliseconds ?? 0,
            Gain: exposure?.Gain ?? 0,
            Status: status);
    }

    private static AllSkyRigSummary? CreateRigSummary(RigSpec? rig)
    {
        if (rig is null)
        {
            return null;
        }

        var sensor = new AllSkySensorSummary(
            WidthPx: rig.Sensor.WidthPx,
            HeightPx: rig.Sensor.HeightPx,
            PixelSizeMicrons: rig.Sensor.PixelSizeMicrons,
            Status: "Configured");

        var lens = new AllSkyLensSummary(
            Name: string.IsNullOrWhiteSpace(rig.Lens.Name) ? rig.Lens.Kind.ToString() : rig.Lens.Name,
            Kind: rig.Lens.Kind.ToString(),
            Model: rig.Lens.Model.ToString(),
            FocalLengthMm: rig.Lens.FocalLengthMm,
            FovXDeg: rig.Lens.FovXDeg,
            FovYDeg: rig.Lens.FovYDeg,
            Status: "Configured");

        var status = "Configured";

        return new AllSkyRigSummary(
            Name: rig.Name,
            Sensor: sensor,
            Lens: lens,
            Status: status);
    }
}
