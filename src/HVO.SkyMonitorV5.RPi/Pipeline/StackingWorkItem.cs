using System;
using HVO.SkyMonitorV5.RPi.Models;

namespace HVO.SkyMonitorV5.RPi.Pipeline;

public sealed record StackingWorkItem(
    int FrameNumber,
    CapturedImage Capture,
    CameraConfiguration ConfigurationSnapshot,
    int ConfigurationVersion,
    DateTimeOffset EnqueuedAt);
