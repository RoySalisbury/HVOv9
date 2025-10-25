#nullable enable
using System;
using HVO.SkyMonitorV5.RPi.Cameras.Optics;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;

namespace HVO.SkyMonitorV5.RPi.Cameras.Projection
{
    /// <summary>
    /// Fisheye projector implementing common fisheye mappings (Equidistant, EquisolidAngle, Orthographic, Stereographic).
    /// Maps camera unit rays to pixel coordinates using a given diagonal/vertical/horizontal FOV.
    /// </summary>
    public sealed class FisheyeProjector : IImageProjector
    {
        public int WidthPx { get; }
        public int HeightPx { get; }
        public double Cx { get; }
        public double Cy { get; }
        public ProjectionModel Model { get; }
        public double FovDeg { get; }           // total FOV (use horizontal for typical video frames)
        public double HorizonPadding { get; }   // scale factor for max radius (e.g., 0.98)

        public FisheyeProjector(int widthPx, int heightPx, ProjectionModel model, double fovDeg, double? cx = null, double? cy = null, double horizonPadding = 0.98)
        {
            if (widthPx <= 0 || heightPx <= 0) throw new ArgumentOutOfRangeException(nameof(widthPx));
            WidthPx = widthPx;
            HeightPx = heightPx;
            Cx = cx ?? widthPx * 0.5;
            Cy = cy ?? heightPx * 0.5;
            Model = model;
            FovDeg = Math.Clamp(fovDeg, 30.0, 200.0);
            HorizonPadding = Math.Clamp(horizonPadding, 0.8, 1.05);
        }

        public bool TryProjectRay(double X, double Y, double Z, out float u, out float v)
        {
            u = v = 0f;
            // normalize
            var len = Math.Sqrt(X*X + Y*Y + Z*Z);
            if (len == 0) return false;
            X/=len; Y/=len; Z/=len;

            // theta = angle from optical axis (+Z)
            var theta = Math.Acos(Math.Clamp(Z, -1.0, 1.0));
            var thetaMax = (FovDeg * Math.PI / 180.0) * 0.5;
            if (theta > thetaMax) return false; // outside FOV

            // radial mapping
            double rPrime = Model switch
            {
                ProjectionModel.Equidistant     => theta / thetaMax,
                ProjectionModel.EquisolidAngle  => Math.Sin(theta / 2.0) / Math.Sin(thetaMax / 2.0),
                ProjectionModel.Orthographic    => Math.Sin(theta) / Math.Sin(thetaMax),
                ProjectionModel.Stereographic   => Math.Tan(theta / 2.0) / Math.Tan(thetaMax / 2.0),
                _ => theta / thetaMax // fallback
            };
            rPrime = Math.Min(rPrime, 1.0);

            var maxRadius = Math.Min(Cx, Cy) * HorizonPadding; // circle inscribed in the image
            var radius = rPrime * maxRadius;

            // direction in image plane
            var denom = Math.Sqrt(X*X + Y*Y);
            double ux = 0, uy = 0;
            if (denom > 0)
            {
                ux = X / denom;
                uy = Y / denom;
            }
            // pixel coordinates (y-down)
            var uu = Cx + radius * ux;
            var vv = Cy - radius * uy;

            if (uu < 0 || uu >= WidthPx || vv < 0 || vv >= HeightPx) return false;
            u = (float)uu;
            v = (float)vv;
            return true;
        }

        public (double X, double Y, double Z) PixelToRay(float u, float v)
        {
            // From pixel to unit ray: invert radial mapping (approximate).
            var dx = u - Cx;
            var dy = Cy - v;
            var maxRadius = Math.Min(Cx, Cy) * HorizonPadding;
            var r = Math.Sqrt(dx*dx + dy*dy);
            if (r == 0) return (0, 0, 1);

            var rPrime = Math.Min(r / maxRadius, 1.0);
            var thetaMax = (FovDeg * Math.PI / 180.0) * 0.5;
            double theta = Model switch
            {
                ProjectionModel.Equidistant     => rPrime * thetaMax,
                ProjectionModel.EquisolidAngle  => 2.0 * Math.Asin(rPrime * Math.Sin(thetaMax / 2.0)),
                ProjectionModel.Orthographic    => Math.Asin(rPrime * Math.Sin(thetaMax)),
                ProjectionModel.Stereographic   => 2.0 * Math.Atan(rPrime * Math.Tan(thetaMax / 2.0)),
                _ => rPrime * thetaMax
            };

            var sinTheta = Math.Sin(theta);
            var cosTheta = Math.Cos(theta);
            var ux = dx / r;
            var uy = dy / r;

            // reconstruct camera ray: Z = cos(theta), X/Y proportional to sin(theta) * direction
            var X = ux * sinTheta;
            var Y = uy * sinTheta;
            var Z = cosTheta;

            var norm = Math.Sqrt(X*X + Y*Y + Z*Z);
            return (X/norm, Y/norm, Z/norm);
        }
    }
}
