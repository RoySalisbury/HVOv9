#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Models;

namespace HVO.SkyMonitorV5.RPi.Services.RemoteDispatch;

public interface IRemoteFramePublisher
{
    Task<RemoteDispatchResult> PublishAsync(RemoteFrameEnvelope envelope, CancellationToken cancellationToken);
}

public sealed record RemoteFrameEnvelope(
    int FrameNumber,
    CapturedImage CapturedFrame,
    RigSpec Rig,
    CameraConfiguration Configuration,
    int ConfigurationVersion,
    bool UsingBackgroundStacker,
    double CaptureMilliseconds,
    DateTimeOffset CapturedAtLocal,
    DateTimeOffset CapturedAtUtc);

public sealed record RemoteDispatchResult(
    RemoteDispatchOutcome Outcome,
    string Mode,
    string? Message = null,
    Exception? Error = null,
    RemoteDispatchEventMetrics? Metrics = null)
{
    public static RemoteDispatchResult Disabled(string mode, string? message = null)
        => new(RemoteDispatchOutcome.Disabled, mode, message);

    public static RemoteDispatchResult Skipped(string mode, string? message = null)
        => new(RemoteDispatchOutcome.Skipped, mode, message);

    public static RemoteDispatchResult Success(string mode, string? message = null, RemoteDispatchEventMetrics? metrics = null)
        => new(RemoteDispatchOutcome.Succeeded, mode, message, null, metrics);

    public static RemoteDispatchResult Failure(string mode, string message, Exception? error = null, RemoteDispatchEventMetrics? metrics = null)
        => new(RemoteDispatchOutcome.Failed, mode, message, error, metrics);
}
