#nullable enable
using System;
using HVO.SkyMonitorV5.RPi.Cameras.Optics;

namespace HVO.SkyMonitorV5.RPi.Cameras.Projection
{
    /// <summary>
    /// Creates concrete <see cref="IImageProjector"/> instances from a <see cref="RigSpec"/>.
    /// Decides between fisheye and rectilinear pipelines based on <see cref="LensSpec.Kind"/>.
    /// </summary>
    public static class RigFactory
    {
        /// <summary>
        /// Build an image projector from the given rig.
        /// For fisheye, uses <see cref="FisheyeProjector"/> with the lens model/FOV.
        /// For rectilinear/telescope, builds a <see cref="RectilinearLens"/> (physical path) and wraps it.
        /// </summary>
        /// <param name="rig">Camera+Lens pair.</param>
        /// <param name="horizonPadding">Fisheye only: scale for inscribed image circle relative to frame.</param>
        /// <param name="overrideCx">Optional principal point X; defaults to image center.</param>
        /// <param name="overrideCy">Optional principal point Y; defaults to image center.</param>
        public static IImageProjector CreateProjector(RigSpec rig, double horizonPadding = 0.98, double? overrideCx = null, double? overrideCy = null)
        {
            var sensor = rig.Sensor;
            double cx = overrideCx ?? sensor.Cx;
            double cy = overrideCy ?? sensor.Cy;

            switch (rig.Lens.Kind)
            {
                case LensKind.Fisheye:
                {
                    // pick FOV: prefer horizontal if supplied; otherwise fall back to diagonal, or 180.
                    // Your LensSpec stores H/V; if only H is set, it’s fine to use it directly.
                    var fov = rig.Lens.FovXDeg > 0 ? rig.Lens.FovXDeg :
                              rig.Lens.FovYDeg.HasValue ? rig.Lens.FovYDeg.Value : 180.0;

                    return new FisheyeProjector(
                        widthPx: sensor.WidthPx,
                        heightPx: sensor.HeightPx,
                        model: rig.Lens.Model,
                        fovDeg: fov,
                        cx: cx, cy: cy,
                        horizonPadding: horizonPadding);
                }

                case LensKind.Rectilinear:
                case LensKind.Telescope:
                default:
                {
                    // Physical rectilinear path using focal length + pixel pitch.
                    if (rig.Lens.FocalLengthMm <= 0) throw new ArgumentOutOfRangeException(nameof(rig), "Lens focal length must be > 0 for rectilinear/telescope.");

                    var lens = RectilinearLens.FromPhysical(
                        widthPx: sensor.WidthPx,
                        heightPx: sensor.HeightPx,
                        focalLengthMm: rig.Lens.FocalLengthMm,
                        pixelPitchUm: sensor.PixelPitchUm,
                        cx: cx, cy: cy,
                        model: rig.Lens.Model);

                    return new RectilinearProjector(lens);
                }
            }
        }
    }
}
