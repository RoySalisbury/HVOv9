using System.Collections.Generic;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Models;

namespace HVO.SkyMonitorV5.RPi.Storage;

public interface IFrameStateStore
{
    CameraConfiguration Configuration { get; }

    int ConfigurationVersion { get; }

    CameraDescriptor? CameraDescriptor { get; }

    RigSpec? Rig { get; }

    ProcessedFrame? LatestProcessedFrame { get; }

    RawFrameSnapshot? LatestRawFrame { get; }

    DateTimeOffset? LastFrameTimestamp { get; }

    ExposureAnalysisResult? LatestExposureAnalysis { get; }

    DateTimeOffset? LatestExposureAnalysisTimestamp { get; }

    ExposureOverrideState? DayExposureOverride { get; }

    ExposureOverrideState? NightExposureOverride { get; }

    bool IsRunning { get; }

    Exception? LastError { get; }

    BackgroundStackerStatus? BackgroundStackerStatus { get; }

    CapturePacingStatus? CapturePacingStatus { get; }

    ProcessingQueueStatus? ProcessingQueueStatus { get; }

    RemoteDispatchStatus? RemoteDispatchStatus { get; }

    RemoteDispatchMetricsSnapshot? RemoteDispatchMetrics { get; }

    void UpdateConfiguration(CameraConfiguration configuration);

    void UpdateFrame(RawFrameSnapshot rawFrame, ProcessedFrame processedFrame);

    void UpdateRunningState(bool isRunning);

    void UpdateRig(RigSpec rig);

    void SetLastError(Exception? exception);

    void UpdateExposureAnalysis(ExposureAnalysisResult analysis, DateTimeOffset capturedAtUtc);

    void UpdateBackgroundStackerStatus(BackgroundStackerStatus status);

    void UpdateCapturePacingStatus(CapturePacingStatus status);

    void UpdateProcessingQueueStatus(ProcessingQueueStatus status);

    void UpdateRemoteDispatchStatus(RemoteDispatchStatus status, RemoteDispatchEventMetrics eventMetrics);

    void UpdateExposureOverride(ExposureOverrideUpdate update);

    IReadOnlyList<BackgroundStackerHistorySample> GetBackgroundStackerHistory();

    IReadOnlyList<RemoteDispatchHistorySample> GetRemoteDispatchHistory();

    AllSkyStatusResponse GetStatus();
}
