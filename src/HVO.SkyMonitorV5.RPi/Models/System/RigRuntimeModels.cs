using System;
using HVO.SkyMonitorV5.RPi.Cameras.Acquisition;

namespace HVO.SkyMonitorV5.RPi.Models.System;

#nullable enable

public enum RigRuntimeActionKind
{
    Start,
    Pause,
    Resume,
    Stop,
    Reload
}

public sealed record RigRuntimeControlCapabilities(
    bool CanStart,
    bool CanPause,
    bool CanResume,
    bool CanStop,
    bool CanReload,
    bool CanForceReload);

public sealed record RigRuntimeStatusResponse(
    RigAdapterLifecycleState State,
    RigRuntimeControlCapabilities Capabilities,
    string RigName,
    string CameraName,
    string DriverIdentifier,
    string AdapterName,
    DateTimeOffset TimestampUtc,
    string? Message);

public sealed record RigRuntimeActionResponse(
    RigRuntimeActionKind Action,
    bool ForceRestart,
    bool Succeeded,
    bool StateChanged,
    string Message,
    RigRuntimeStatusResponse Status,
    DateTimeOffset CompletedAtUtc);

public sealed record RigRuntimeActionRequest
{
    public RigRuntimeActionKind Action { get; init; }

    public bool ForceRestart { get; init; }
}
