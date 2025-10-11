#nullable enable
using System;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;

namespace HVO.SkyMonitorV5.RPi.Cameras.Optics
{
    /// <summary>
    /// Camera body specification (sensor characteristics). Use with <see cref="LensSpec"/> to form a "rig".
    /// </summary>
    public sealed record CameraSpec(
        string Name,
        SensorSpec Sensor,
        CameraCapabilities Capabilities)
    {
        public CameraSpec(string name, SensorSpec sensor)
            : this(name, sensor, CameraCapabilities.Empty)
        {
        }
    }


    /// <summary>
    /// Handy presets for common cameras and lenses used in this project.
    /// <para>
    /// NOTE on LensSpec semantics (matches your existing record):
    /// <code>
    /// public sealed record LensSpec(
    ///     ProjectionModel Model,   // Equidistant, Equisolid, Perspective, ...
    ///     double FocalLengthMm,    // 2.7 for FE185C086HA-1 (fisheye); telescope FL otherwise
    ///     double FovXDeg,          // for convenience & override; derive if unknown
    ///     double? FovYDeg = null,  // optional
    ///     double RollDeg = 0.0,    // camera roll about optical axis
    ///     string Name = "");
    /// </code>
    /// For rectilinear/telescope setups, set <c>FovXDeg</c> to 0.0 to indicate it should be
    /// derived from (focal length, sensor size). For fisheyes, we often fill horizontal/vertical
    /// FOVs from the datasheet and keep the mapping model (e.g., EquisolidAngle).
    /// </summary>
    public static class OpticsPresets
    {
        // ---------------- Cameras ----------------

        /// <summary>
        /// ZWO ASI174MM (Sony IMX174): 1936×1216, 5.86 µm pixels.
        /// </summary>
        public static readonly CameraSpec ASI174MM = new(
            Name: "ZWO ASI174MM",
            Sensor: new SensorSpec(
                WidthPx: 1936,
                HeightPx: 1216,
                PixelSizeMicrons: 5.86
            ),
            Capabilities: new CameraCapabilities(
                colorMode: CameraColorMode.Monochrome,
                sensorTechnology: CameraSensorTechnology.Cmos,
                bodyType: CameraBodyType.DedicatedAstronomy,
                cooling: CameraCoolingType.None,
                supportsGainControl: true,
                supportsExposureControl: true,
                supportsTemperatureTelemetry: false,
                supportsSoftwareBinning: true)
        );

        /// <summary>
        /// Mock ASI174MM – same sensor; use when driving the synthetic camera.
        /// </summary>
        public static readonly CameraSpec MockASI174MM = new(
            Name: "Mock ASI174MM",
            Sensor: new SensorSpec(
                WidthPx: 1936,
                HeightPx: 1216,
                PixelSizeMicrons: 5.86
            ),
            Capabilities: new CameraCapabilities(
                colorMode: CameraColorMode.Monochrome,
                sensorTechnology: CameraSensorTechnology.Cmos,
                bodyType: CameraBodyType.Synthetic,
                cooling: CameraCoolingType.None,
                supportsGainControl: true,
                supportsExposureControl: true,
                supportsTemperatureTelemetry: false,
                supportsSoftwareBinning: true)
        );

        /// <summary>
        /// ZWO ASI174MC (colour) – identical geometry but a Bayer CFA.
        /// </summary>
        public static readonly CameraSpec ASI174MC = new(
            Name: "ZWO ASI174MC",
            Sensor: new SensorSpec(
                WidthPx: 1936,
                HeightPx: 1216,
                PixelSizeMicrons: 5.86
            ),
            Capabilities: new CameraCapabilities(
                colorMode: CameraColorMode.Color,
                sensorTechnology: CameraSensorTechnology.Cmos,
                bodyType: CameraBodyType.DedicatedAstronomy,
                cooling: CameraCoolingType.None,
                supportsGainControl: true,
                supportsExposureControl: true,
                supportsTemperatureTelemetry: false,
                supportsSoftwareBinning: true)
        );

        /// <summary>
        /// Mock ASI174MC – colour variant for the synthetic camera.
        /// </summary>
        public static readonly CameraSpec MockASI174MC = new(
            Name: "Mock ASI174MC",
            Sensor: new SensorSpec(
                WidthPx: 1936,
                HeightPx: 1216,
                PixelSizeMicrons: 5.86
            ),
            Capabilities: new CameraCapabilities(
                colorMode: CameraColorMode.Color,
                sensorTechnology: CameraSensorTechnology.Cmos,
                bodyType: CameraBodyType.Synthetic,
                cooling: CameraCoolingType.None,
                supportsGainControl: true,
                supportsExposureControl: true,
                supportsTemperatureTelemetry: false,
                supportsSoftwareBinning: true)
        );

        // ---------------- Lenses / Telescopes ----------------

        /// <summary>
        /// Fujinon FE185C086HA-1 — 1&quot; fisheye, f≈2.7 mm, f/1.8, diagonal FOV ≈185°.
        /// Datasheet typicals for 1&quot; sensor: H≈146°, V≈94° (approx). Mapping for most
        /// photographic fisheyes is close to Equisolid, but calibrate to be sure.
        /// </summary>
        public static readonly LensSpec Fujinon_FE185C086HA_1 = new(
            Model: ProjectionModel.EquisolidAngle,
            FocalLengthMm: 2.7,
            FovXDeg: 146.0,          // horizontal FOV (approx; adjust via calibration if needed)
            FovYDeg: 94.0,
            RollDeg: 0.0,
            Name: "Fujinon FE185C086HA-1 (1\\\")",
            Kind: LensKind.Fisheye
        );

        /// <summary>
        /// Explore Scientific 152 CF APO (example). Assumes f/8 native (~1216 mm); update if your tube differs
        /// (f/7, f/7.5, reducer, etc.). Set FovXDeg = 0 to derive from sensor + focal length.
        /// </summary>
        public static readonly LensSpec ExploreScientific_152CF = new(
            Model: ProjectionModel.Perspective,
            FocalLengthMm: 1216.0,
            FovXDeg: 0.0,            // derive from sensor + focal length
            FovYDeg: null,
            RollDeg: 0.0,
            Name: "Explore Scientific 152 CF APO (assumed f/8)",
            Kind: LensKind.Telescope
        );

        /// <summary>
        /// Generic 50 mm rectilinear lens; FOV derived from sensor + focal length.
        /// </summary>
        public static readonly LensSpec Generic50mm = new(
            Model: ProjectionModel.Perspective,
            FocalLengthMm: 50.0,
            FovXDeg: 0.0,            // derive
            FovYDeg: null,
            RollDeg: 0.0,
            Name: "Generic 50mm",
            Kind: LensKind.Rectilinear
        );
    }

public enum LensKind
    {
        Fisheye,
        Rectilinear,   // normal photographic lens
        Telescope      // rectilinear, typically very long focal length
    }    
}
