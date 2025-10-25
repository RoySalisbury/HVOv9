
#nullable enable
using System;
using HVO.SkyMonitorV5.RPi.Cameras.Optics;
using HVO.SkyMonitorV5.RPi.Cameras.Lenses;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;

namespace HVO.SkyMonitorV5.RPi.Cameras.Projection
{
    /// <summary>
    /// Immutable configuration used to create a <see cref="CelestialProjectionContext"/>.
    /// Backwards compatible with the existing projection-based pipeline, and augmented with
    /// optional optics metadata (sensor/lens) for future rectilinear/telescope modes.
    /// </summary>
    public sealed class CelestialProjectionSettings
    {
        /// <param name="width">Output width in pixels.</param>
        /// <param name="height">Output height in pixels.</param>
        /// <param name="latitudeDegrees">Observer latitude in degrees.</param>
        /// <param name="longitudeDegrees">Observer longitude in degrees (east +).</param>
    /// <param name="projection">Projection model.</param>
        /// <param name="horizonPaddingPercent">Fraction [0..1] of the inscribed circle to use (e.g., 0.98).</param>
    /// <param name="fieldOfViewDegrees">Diagonal field-of-view in degrees (e.g., 180–190 for a full-sky lens).</param>
        /// <param name="applyRefraction">Apply atmospheric refraction when mapping Alt/Az to the image.</param>
        /// <param name="flipHorizontal">Mirror image horizontally (swap E/W) to match the physical camera.</param>
        /// <param name="rollDeg">Clockwise image roll (degrees) about the center; reserved for future use.</param>
        public CelestialProjectionSettings(
            int width,
            int height,
            double latitudeDegrees,
            double longitudeDegrees,
            ProjectionModel projection,
            double horizonPaddingPercent,
            double fieldOfViewDegrees,
            bool applyRefraction,
            bool flipHorizontal,
            double rollDeg = 0.0)
        {
            if (width <= 0)  throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

            Width = width;
            Height = height;
            LatitudeDegrees = latitudeDegrees;
            LongitudeDegrees = longitudeDegrees;
            Projection = projection;
            HorizonPaddingPercent = horizonPaddingPercent;
            FieldOfViewDegrees = fieldOfViewDegrees;
            ApplyRefraction = applyRefraction;
            FlipHorizontal = flipHorizontal;
            RollDegrees = rollDeg;
        }

        /// <summary>Output width in pixels.</summary>
        public int Width { get; }
        /// <summary>Output height in pixels.</summary>
        public int Height { get; }

        /// <summary>Observer latitude in degrees.</summary>
        public double LatitudeDegrees { get; }
        /// <summary>Observer longitude in degrees (east +).</summary>
        public double LongitudeDegrees { get; }

    /// <summary>Projection model for the current render.</summary>
    public ProjectionModel Projection { get; }

        /// <summary>Fraction [0..1] of inscribed circle used (e.g., 0.98 pads away from frame edge).</summary>
        public double HorizonPaddingPercent { get; }

        /// <summary>Diagonal field-of-view in degrees.</summary>
        public double FieldOfViewDegrees { get; }

        /// <summary>Apply atmospheric refraction in mapping Alt/Az to pixel space.</summary>
        public bool ApplyRefraction { get; }

        /// <summary>Mirror image horizontally (swap E/W).</summary>
        public bool FlipHorizontal { get; }

        /// <summary>Clockwise image roll (degrees) about center. Reserved for future expansion.</summary>
        public double RollDegrees { get; }

    // --------- Optional optics metadata (does not affect current projection math) ---------

        /// <summary>Optional physical sensor description used for lens-based configuration.</summary>
        public SensorSpec? Sensor { get; init; }

        /// <summary>Optional lens model used to derive the settings.</summary>
        public ILens? Lens { get; init; }

        /// <summary>Optional unified projection model that produced <see cref="Projection"/>.</summary>
        public ProjectionModel? ProjectionModelOverride { get; init; }

        /// <summary>Convenience: image center X in pixels.</summary>
        public float CenterX => (float)(Width * 0.5);
        /// <summary>Convenience: image center Y in pixels.</summary>
        public float CenterY => (float)(Height * 0.5);
        /// <summary>Convenience: max usable radius (pixels) after padding.</summary>
        public float MaxRadius => (float)(Math.Min(CenterX, CenterY) * HorizonPaddingPercent);

        /// <summary>
    /// Factory: build settings from a <see cref="SensorSpec"/> and <see cref="ILens"/>.
    /// Backed by the current projection pipeline; rectilinear modes are reserved for future.
        /// </summary>
        public static CelestialProjectionSettings From(
            SensorSpec sensor,
            ILens lens,
            double latitudeDeg,
            double longitudeDeg,
            double fovDeg,
            bool flipHorizontal,
            double horizonPaddingPct = 0.98,
            bool applyRefraction = true,
            double rollDeg = 0.0)
        {
            var projectionModel = lens.Model switch
            {
                ProjectionModel.Equidistant or
                ProjectionModel.Orthographic or
                ProjectionModel.Stereographic => lens.Model,
                ProjectionModel.EquisolidAngle => ProjectionModel.Equidistant, // fallback until engine adds equisolid
                _ => ProjectionModel.Equidistant
            };

            return new CelestialProjectionSettings(
                width: sensor.WidthPx,
                height: sensor.HeightPx,
                latitudeDegrees: latitudeDeg,
                longitudeDegrees: longitudeDeg,
                projection: projectionModel,
                horizonPaddingPercent: horizonPaddingPct,
                fieldOfViewDegrees: fovDeg,
                applyRefraction: applyRefraction,
                flipHorizontal: flipHorizontal,
                rollDeg: rollDeg)
            {
                Sensor = sensor,
                Lens = lens,
                ProjectionModelOverride = lens.Model
            };
        }
    }
}
