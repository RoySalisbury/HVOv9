
#nullable enable
using System;
using HVO.SkyMonitorV5.RPi.Cameras.Optics;
using HVO.SkyMonitorV5.RPi.Cameras.Lenses;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;

namespace HVO.SkyMonitorV5.RPi.Cameras.Projection
{
    /// <summary>
    /// Helper to build <see cref="CelestialProjectionSettings"/> from a Sensor + Lens combo.
    /// Currently supports projection models handled by the rendering engine; rectilinear is reserved for a future update.
    /// </summary>
    public static class ProjectionConfigurator
    {
        /// <summary>
        /// Build settings when latitude/longitude are known.
        /// </summary>
        public static CelestialProjectionSettings Build(
            SensorSpec sensor,
            ILens lens,
            double latitudeDeg,
            double longitudeDeg,
            double fovDegOverride,
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
                ProjectionModel.EquisolidAngle => ProjectionModel.Equidistant, // fallback until engine implements equisolid
                _ => ProjectionModel.Equidistant
            };

            return new CelestialProjectionSettings(
                width: sensor.WidthPx,
                height: sensor.HeightPx,
                latitudeDegrees: latitudeDeg,
                longitudeDegrees: longitudeDeg,
                projection: projectionModel,
                horizonPaddingPercent: horizonPaddingPct,
                fieldOfViewDegrees: fovDegOverride,
                applyRefraction: applyRefraction,
                flipHorizontal: flipHorizontal,
                rollDeg: rollDeg)
            {
                Sensor = sensor,
                Lens = lens,
                ProjectionModelOverride = lens.Model
            };
        }

        /// <summary>
        /// Legacy overload (no lat/long). Use when only raster and optics are needed.
        /// </summary>
        public static CelestialProjectionSettings Build(
            SensorSpec sensor,
            ILens lens,
            double fovDegOverride,
            bool flipHorizontal,
            double rollDeg = 0.0)
        {
            var projectionModel = lens.Model switch
            {
                ProjectionModel.Equidistant or
                ProjectionModel.Orthographic or
                ProjectionModel.Stereographic => lens.Model,
                ProjectionModel.EquisolidAngle => ProjectionModel.Equidistant,
                _ => ProjectionModel.Equidistant
            };

            return new CelestialProjectionSettings(
                width: sensor.WidthPx,
                height: sensor.HeightPx,
                latitudeDegrees: 0.0,
                longitudeDegrees: 0.0,
                projection: projectionModel,
                horizonPaddingPercent: 0.98,
                fieldOfViewDegrees: fovDegOverride,
                applyRefraction: true,
                flipHorizontal: flipHorizontal,
                rollDeg: rollDeg)
            {
                Sensor = sensor,
                Lens = lens,
                ProjectionModelOverride = lens.Model
            };
        }

        /// <summary>Compute diagonal FOV (degrees) for a rectilinear lens + sensor.</summary>
        public static double ComputeRectilinearDiagonalFovDeg(SensorSpec sensor, RectilinearLens lens)
        {
            var fPx = lens.Fx;
            var halfDiag = 0.5 * (new System.Numerics.Vector2((float)sensor.WidthPx, (float)sensor.HeightPx)).Length();
            var fovRad = 2.0 * Math.Atan(halfDiag / fPx);
            return fovRad * 180.0 / Math.PI;
        }
    }
}
