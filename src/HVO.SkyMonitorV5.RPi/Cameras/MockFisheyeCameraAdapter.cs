#nullable enable
using System;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;

namespace HVO.SkyMonitorV5.RPi.Cameras;

/// <summary>
/// Compatibility shim that preserves constant access for legacy references.
/// </summary>
[Obsolete("MockFisheyeCameraAdapter has been replaced by MockCameraAdapter.", DiagnosticId = "HVO0001")]
public static class MockFisheyeCameraAdapter
{
	public const ProjectionModel DefaultProjectionModel = MockCameraAdapter.DefaultProjectionModel;
	public const double DefaultHorizonPadding = MockCameraAdapter.DefaultHorizonPadding;
	public const double DefaultFovDeg = MockCameraAdapter.DefaultFovDeg;
}
