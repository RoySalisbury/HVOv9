#nullable enable
using System;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;

namespace HVO.SkyMonitorV5.RPi.Cameras.Optics
{
    /// <summary>
    /// Rectilinear (pinhole) lens model for telescope / normal camera optics.
    /// Supports two parameterizations:
    /// <list type="bullet">
    /// <item><description><b>Physical:</b> focal length in mm + pixel pitch in µm (preferred).</description></item>
    /// <item><description><b>FOV fallback:</b> horizontal FOV deg (derives intrinsics from image aspect).</description></item>
    /// </list>
    /// Camera frame: +X right, +Y up, +Z forward. Pixel (0,0) top-left. Principal point (Cx,Cy).
    /// Forward (Perspective/Gnomonic): u = fx * (X/Z) + Cx, v = -fy * (Y/Z) + Cy, valid if Z &gt; 0.
    /// </summary>
    public sealed class RectilinearLens
    {
        public int WidthPx  { get; }
        public int HeightPx { get; }

        /// <summary>Principal point in pixels.</summary>
        public double Cx { get; }
        /// <summary>Principal point in pixels.</summary>
        public double Cy { get; }

        /// <summary>Focal length in pixels along X.</summary>
        public double Fx { get; }
        /// <summary>Focal length in pixels along Y.</summary>
        public double Fy { get; }

        /// <summary>Compatibility helper for older call sites that used <c>FocalPx()</c> as a method.</summary>
        public double FocalPx() => Fx;

        /// <summary>Horizontal field of view in degrees (derived when using physical params).</summary>
        public double FovXDeg { get; }
        /// <summary>Vertical field of view in degrees (derived when using physical params).</summary>
        public double FovYDeg { get; }

        /// <summary>Physical focal length in mm (0 if unknown).</summary>
        public double FocalLengthMm { get; }
        /// <summary>Pixel pitch in µm (0 if unknown).</summary>
        public double PixelPitchUm { get; }

        public ProjectionModel Model { get; }

        /// <summary>Create using physical parameters (preferred).</summary>
        public static RectilinearLens FromPhysical(
            int widthPx,
            int heightPx,
            double focalLengthMm,
            double pixelPitchUm,
            double? cx = null,
            double? cy = null,
            ProjectionModel model = ProjectionModel.Perspective)
        {
            if (widthPx <= 0 || heightPx <= 0) throw new ArgumentOutOfRangeException(nameof(widthPx));
            if (focalLengthMm <= 0) throw new ArgumentOutOfRangeException(nameof(focalLengthMm));
            if (pixelPitchUm <= 0) throw new ArgumentOutOfRangeException(nameof(pixelPitchUm));

            var pitchMm = pixelPitchUm / 1000.0;
            var fpx = focalLengthMm / pitchMm; // px = mm / (mm/px)

            var cxv = cx ?? widthPx * 0.5;
            var cyv = cy ?? heightPx * 0.5;

            // Derive FOVs from intrinsics
            var halfXFov = Math.Atan((widthPx * 0.5) / fpx);
            var halfYFov = Math.Atan((heightPx * 0.5) / fpx);

            return new RectilinearLens(
                widthPx, heightPx,
                cxv, cyv,
                fx: fpx, fy: fpx,
                fovXDeg: 2.0 * halfXFov * 180.0 / Math.PI,
                fovYDeg: 2.0 * halfYFov * 180.0 / Math.PI,
                focalLengthMm: focalLengthMm,
                pixelPitchUm: pixelPitchUm,
                model: NormalizeRectilinear(model));
        }

        /// <summary>Create using horizontal FOV (deg). fy is computed from aspect ratio.</summary>
        public static RectilinearLens FromFov(
            int widthPx,
            int heightPx,
            double fovXDeg,
            double? cx = null,
            double? cy = null,
            ProjectionModel model = ProjectionModel.Perspective)
        {
            if (widthPx <= 0 || heightPx <= 0) throw new ArgumentOutOfRangeException(nameof(widthPx));
            var fovX = Math.Clamp(fovXDeg, 1.0, 179.0) * Math.PI / 180.0;
            var halfX = fovX * 0.5;

            var fx = (widthPx * 0.5) / Math.Tan(halfX);
            var halfY = Math.Atan((heightPx / (double)widthPx) * Math.Tan(halfX));
            var fy = (heightPx * 0.5) / Math.Tan(halfY);

            var cxv = cx ?? widthPx * 0.5;
            var cyv = cy ?? heightPx * 0.5;

            return new RectilinearLens(
                widthPx, heightPx,
                cxv, cyv,
                fx, fy,
                fovXDeg: fovXDeg,
                fovYDeg: 2.0 * halfY * 180.0 / Math.PI,
                focalLengthMm: 0.0,
                pixelPitchUm: 0.0,
                model: NormalizeRectilinear(model));
        }

        private RectilinearLens(
            int widthPx, int heightPx,
            double cx, double cy,
            double fx, double fy,
            double fovXDeg, double fovYDeg,
            double focalLengthMm, double pixelPitchUm,
            ProjectionModel model)
        {
            WidthPx = widthPx;
            HeightPx = heightPx;
            Cx = cx;
            Cy = cy;
            Fx = fx;
            Fy = fy;
            FovXDeg = fovXDeg;
            FovYDeg = fovYDeg;
            FocalLengthMm = focalLengthMm;
            PixelPitchUm = pixelPitchUm;
            Model = model;
        }

        public bool TryProjectCameraRay(double X, double Y, double Z, out float u, out float v)
        {
            u = v = 0f;
            if (!IsRectilinear(Model)) return false;
            if (Z <= 0.0) return false;

            var uu = Fx * (X / Z) + Cx;
            var vv = -Fy * (Y / Z) + Cy;

            if (uu < 0 || uu >= WidthPx || vv < 0 || vv >= HeightPx) return false;
            u = (float)uu;
            v = (float)vv;
            return true;
        }

        public (double X, double Y, double Z) PixelToCameraRay(float u, float v)
        {
            var xn = (u - Cx) / Fx;
            var yn = (Cy - v) / Fy;
            var len = Math.Sqrt(xn * xn + yn * yn + 1.0);
            return (xn / len, yn / len, 1.0 / len);
        }

        public static (double FovXDeg, double FovYDeg) ComputeFovFromIntrinsics(int widthPx, int heightPx, double fx, double fy)
        {
            var halfXFov = Math.Atan((widthPx * 0.5) / fx);
            var halfYFov = Math.Atan((heightPx * 0.5) / fy);
            return (2.0 * halfXFov * 180.0 / Math.PI, 2.0 * halfYFov * 180.0 / Math.PI);
        }

        private static bool IsRectilinear(ProjectionModel m) =>
            m == ProjectionModel.Perspective || m == ProjectionModel.Gnomonic;

        private static ProjectionModel NormalizeRectilinear(ProjectionModel m) =>
            IsRectilinear(m) ? m : ProjectionModel.Perspective;
    }
}
