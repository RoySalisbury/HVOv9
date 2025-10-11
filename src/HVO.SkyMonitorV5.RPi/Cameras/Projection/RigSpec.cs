#nullable enable
using HVO.SkyMonitorV5.RPi.Cameras.Optics;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;
using HVO.SkyMonitorV5.RPi.Models;

namespace HVO.SkyMonitorV5.RPi.Cameras.Projection;

/// <summary>
/// A complete camera+lens configuration the app can use at runtime.
/// </summary>
public sealed record RigSpec(
    string Name,
    CameraSpec Camera,
    LensSpec Lens,
    CameraDescriptor? Descriptor = null
)
{
    public SensorSpec Sensor => Camera.Sensor;

    public CameraCapabilities Capabilities => Camera.Capabilities;
}

/// <summary>
/// Handy ready-made rigs. Add more as you acquire lenses/cameras.
/// </summary>
public static class RigPresets
{
    /// <summary>
    /// ZWO ASI174MM + Fujinon FE185C086HA-1 (2.7mm, fisheye ~185°).
    ///  - ASI174MM native: 1936 x 1216 px, 5.86 µm pixel pitch.
    /// </summary>
    public static readonly RigSpec MockAsi174_Fujinon = new(
        Name: "MockASI174MM + Fujinon 2.7mm",
        Camera: OpticsPresets.MockASI174MM,
        Lens: new LensSpec(
            Model: ProjectionModel.Equidistant, // common for security fisheyes; adjust if calibration says otherwise
            FocalLengthMm: 2.7,
            FovXDeg: 185.0,
            FovYDeg: 185.0,
            RollDeg: 0.0,
            Name: "Fujinon FE185C086HA-1",
            Kind: LensKind.Fisheye
        ),
        Descriptor: new CameraDescriptor(
            Manufacturer: "HVO",
            Model: "Mock Fisheye AllSky",
            DriverVersion: "2.0.0",
            AdapterName: "MockCameraAdapter",
            Capabilities: new[] { "Synthetic", "StackingCompatible", "FisheyeProjection" }
        )
    );

    /// <summary>
    /// ZWO ASI174MC (colour) + Fujinon FE185C086HA-1.
    /// </summary>
    public static readonly RigSpec MockAsi174MC_Fujinon = new(
        Name: "MockASI174MC + Fujinon 2.7mm",
        Camera: OpticsPresets.MockASI174MC,
        Lens: new LensSpec(
            Model: ProjectionModel.Equidistant,
            FocalLengthMm: 2.7,
            FovXDeg: 185.0,
            FovYDeg: 185.0,
            RollDeg: 0.0,
            Name: "Fujinon FE185C086HA-1",
            Kind: LensKind.Fisheye
        ),
        Descriptor: new CameraDescriptor(
            Manufacturer: "HVO",
            Model: "Mock Colour Fisheye AllSky",
            DriverVersion: "2.0.0",
            AdapterName: "MockColorCameraAdapter",
            Capabilities: new[] { "Synthetic", "StackingCompatible", "FisheyeProjection", "Colour" }
        )
    );
}
