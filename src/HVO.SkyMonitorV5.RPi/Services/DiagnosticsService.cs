using System;
using System.Threading;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Storage;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Services;

public sealed class DiagnosticsService : IDiagnosticsService
{
    private readonly IFrameStateStore _frameStateStore;
    private readonly IFrameFilterPipeline _frameFilterPipeline;
    private readonly ILogger<DiagnosticsService> _logger;
    private readonly IObservatoryClock _clock;

    public DiagnosticsService(
        IFrameStateStore frameStateStore,
        IFrameFilterPipeline frameFilterPipeline,
        ILogger<DiagnosticsService> logger,
        IObservatoryClock clock)
    {
        _frameStateStore = frameStateStore ?? throw new ArgumentNullException(nameof(frameStateStore));
        _frameFilterPipeline = frameFilterPipeline ?? throw new ArgumentNullException(nameof(frameFilterPipeline));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public Task<Result<BackgroundStackerMetricsResponse>> GetBackgroundStackerMetricsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var status = _frameStateStore.BackgroundStackerStatus;
            if (status is null)
            {
                _logger.LogDebug("Background stacker metrics requested before telemetry was available.");
                return Task.FromResult(Result<BackgroundStackerMetricsResponse>.Success(CreateEmptyStackerMetrics()));
            }

            var response = MapBackgroundStackerMetrics(status);
            return Task.FromResult(Result<BackgroundStackerMetricsResponse>.Success(response));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while gathering background stacker metrics snapshot.");
            return Task.FromResult<Result<BackgroundStackerMetricsResponse>>(ex);
        }
    }

    public Task<Result<FilterMetricsSnapshot>> GetFilterMetricsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_frameFilterPipeline is FrameFilterPipeline pipeline)
            {
                var snapshot = pipeline.GetMetricsSnapshot();
                return Task.FromResult(Result<FilterMetricsSnapshot>.Success(snapshot));
            }

            _logger.LogWarning("Frame filter pipeline implementation did not expose telemetry snapshot; returning empty telemetry.");
            return Task.FromResult(Result<FilterMetricsSnapshot>.Success(new FilterMetricsSnapshot(Array.Empty<FilterMetrics>())));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while gathering filter metrics snapshot.");
            return Task.FromResult<Result<FilterMetricsSnapshot>>(ex);
        }
    }

    public Task<Result<BackgroundStackerHistoryResponse>> GetBackgroundStackerHistoryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var samples = _frameStateStore.GetBackgroundStackerHistory();
            var history = new BackgroundStackerHistoryResponse(_clock.LocalNow, samples);

            return Task.FromResult(Result<BackgroundStackerHistoryResponse>.Success(history));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while gathering background stacker history snapshot.");
            return Task.FromResult<Result<BackgroundStackerHistoryResponse>>(ex);
        }
    }

    private static BackgroundStackerMetricsResponse CreateEmptyStackerMetrics() => new(
        Enabled: false,
        QueueDepth: 0,
        QueueCapacity: 0,
        QueueFillPercentage: 0,
        PeakQueueDepth: 0,
        PeakQueueFillPercentage: 0,
        ProcessedFrameCount: 0,
        DroppedFrameCount: 0,
        QueuePressureLevel: 0,
        LastQueueLatencyMilliseconds: null,
        AverageQueueLatencyMilliseconds: null,
        MaxQueueLatencyMilliseconds: null,
        LastStackMilliseconds: null,
        AverageStackMilliseconds: null,
        LastFilterMilliseconds: null,
        AverageFilterMilliseconds: null,
        QueueMemoryBytes: 0,
        PeakQueueMemoryBytes: 0,
        QueueMemoryMegabytes: 0,
        PeakQueueMemoryMegabytes: 0,
        LastEnqueuedAt: null,
        LastCompletedAt: null,
        SecondsSinceLastCompleted: null,
        LastFrameNumber: null);

    private BackgroundStackerMetricsResponse MapBackgroundStackerMetrics(BackgroundStackerStatus status)
    {
        DateTimeOffset? lastEnqueuedAt = status.LastEnqueuedAt is { } enqueued
            ? _clock.ToLocal(enqueued)
            : null;
        DateTimeOffset? lastCompletedAt = status.LastCompletedAt is { } completed
            ? _clock.ToLocal(completed)
            : null;

        return new BackgroundStackerMetricsResponse(
            Enabled: status.Enabled,
            QueueDepth: status.QueueDepth,
            QueueCapacity: status.QueueCapacity,
            QueueFillPercentage: status.QueueFillPercentage,
            PeakQueueDepth: status.PeakQueueDepth,
            PeakQueueFillPercentage: status.PeakQueueFillPercentage,
            ProcessedFrameCount: status.ProcessedFrameCount,
            DroppedFrameCount: status.DroppedFrameCount,
            QueuePressureLevel: status.QueuePressureLevel,
            LastQueueLatencyMilliseconds: status.LastQueueLatencyMilliseconds,
            AverageQueueLatencyMilliseconds: status.AverageQueueLatencyMilliseconds,
            MaxQueueLatencyMilliseconds: status.MaxQueueLatencyMilliseconds,
            LastStackMilliseconds: status.LastStackMilliseconds,
            AverageStackMilliseconds: status.AverageStackMilliseconds,
            LastFilterMilliseconds: status.LastFilterMilliseconds,
            AverageFilterMilliseconds: status.AverageFilterMilliseconds,
            QueueMemoryBytes: status.QueueMemoryBytes,
            PeakQueueMemoryBytes: status.PeakQueueMemoryBytes,
            QueueMemoryMegabytes: status.QueueMemoryMegabytes,
            PeakQueueMemoryMegabytes: status.PeakQueueMemoryMegabytes,
            LastEnqueuedAt: lastEnqueuedAt,
            LastCompletedAt: lastCompletedAt,
            SecondsSinceLastCompleted: status.SecondsSinceLastCompleted,
            LastFrameNumber: status.LastFrameNumber);
    }
}
