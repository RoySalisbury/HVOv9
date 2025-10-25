#nullable enable
using System;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;
using HVO.SkyMonitorV5.RPi.Cameras.Optics;

namespace HVO.SkyMonitorV5.RPi.Cameras.Projection
{
    /// <summary>
    /// Optional low-level projector that maps alt/az to pixels given a boresight and projection model.
    /// Not registered in DI; use <see cref="CelestialProjector"/> for the production pipeline.
    /// </summary>
    public sealed class BoresightProjector
    {
        private readonly ProjectionMath.ProjectionBasis _basis;
        private readonly double _cx, _cy, _rMax;
        private readonly bool _flipHorizontal;
    private readonly double _rollRad;
    private readonly ProjectionModel _projectionModel;
        private readonly double _f;

        public BoresightProjector(
            int width,
            int height,
            double boresightAltDeg,
            double boresightAzDeg,
            ProjectionModel model,
            double fieldOfViewDeg,
            double horizonPaddingPct,
            bool flipHorizontal,
            double rollDeg = 0.0)
        {
            if (width <= 0)  throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

            _cx = width * 0.5; _cy = height * 0.5;
            _rMax = Math.Min(_cx, _cy) * horizonPaddingPct;

            _basis = ProjectionMath.BuildBasis(boresightAltDeg, boresightAzDeg);
            _flipHorizontal = flipHorizontal;
            _rollRad = rollDeg * Math.PI / 180.0;
            _projectionModel = model;

            var thetaMax = Math.Clamp(fieldOfViewDeg, 1.0, 200.0) * Math.PI / 180.0 * 0.5;
            double g = model switch
            {
                ProjectionModel.Equidistant    => thetaMax,
                ProjectionModel.Orthographic   => Math.Sin(thetaMax),
                ProjectionModel.Stereographic  => 2.0 * Math.Tan(thetaMax/2.0),
                ProjectionModel.EquisolidAngle => 2.0 * Math.Sin(thetaMax/2.0),
                _ => thetaMax
            };
            if (g < 1e-9) g = 1e-9;
            _f = _rMax / g;
        }

        public bool ProjectAltAz(double altDeg, double azDeg, out float x, out float y)
        {
            x = y = 0;
            var d = ProjectionMath.DirFromAltAz(altDeg, azDeg);
            var cosT = Math.Clamp(ProjectionMath.Dot(d, _basis.B), -1.0, 1.0);
            var theta = Math.Acos(cosT);
            var de1 = ProjectionMath.Dot(d, _basis.E1);
            var de2 = ProjectionMath.Dot(d, _basis.E2);
            var phi = Math.Atan2(de1, de2) + _rollRad;

            var r = _projectionModel switch
            {
                ProjectionModel.Equidistant    => _f * theta,
                ProjectionModel.Orthographic   => _f * Math.Sin(theta),
                ProjectionModel.Stereographic  => 2.0 * _f * Math.Tan(theta/2.0),
                ProjectionModel.EquisolidAngle => 2.0 * _f * Math.Sin(theta/2.0),
                _ => _f * theta
            };

            if (r > _rMax + 1e-3) return false;

            var dx = r * Math.Sin(phi);
            var dy = r * Math.Cos(phi);

            x = (float)(_flipHorizontal ? _cx - dx : _cx + dx);
            y = (float)(_cy - dy);
            return true;
        }
    }
}
