#nullable enable
using System;
using HVO.SkyMonitorV5.RPi.Cameras.Projection; // ProjectionMath
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;
using static HVO.SkyMonitorV5.RPi.Cameras.Projection.ProjectionMath;

namespace HVO.SkyMonitorV5.RPi.Cameras.Optics;

/// <summary>
/// A rig-aware projector that maps Alt/Az to sensor pixels using:
/// - Fisheye models (Equidistant, EquisolidAngle, Orthographic, Stereographic) via r(θ)
///   with focal length in *pixels* derived from F in mm and pixel pitch, or by FOV fallback.
/// - Rectilinear (Perspective, Gnomonic) via standard pinhole projection (u=fx*X/Z, v=fy*Y/Z).
/// The boresight (Alt0/Az0) + roll define the camera basis.
/// </summary>
public sealed class DefaultRigProjector : IRigProjector
{
    private readonly CameraRig _rig;
    private readonly int _w, _h;
    private readonly double _cx, _cy;
    private readonly bool _flipX;
    private readonly double _rollRad;
    private readonly ProjectionModel _model;

    // Camera orientation basis
    private readonly ProjectionBasis _basis;

    // Pixel pitch (mm), focal length (mm)
    private readonly double _pitchMm;
    private readonly double _fMm;

    // Rectilinear: focal lengths in pixels
    private readonly double _fx, _fy;

    // Fisheye: effective f in pixels; circle fit fallback via FOV if needed
    private readonly double _fFisheyePx;
    private readonly double _rMax;      // circle fit fallback
    private readonly double _thetaMax;  // half-FOV in radians (for fallback scaling)

    public DefaultRigProjector(
        CameraRig rig,
        double horizonPaddingPct = 0.98,
        bool flipHorizontal = false)
    {
        _rig = rig ?? throw new ArgumentNullException(nameof(rig));
        _w = rig.Sensor.WidthPx;
        _h = rig.Sensor.HeightPx;
        _cx = _w * 0.5;
        _cy = _h * 0.5;
        _flipX = flipHorizontal;
        _rollRad = rig.Lens.RollDeg * Math.PI / 180.0;
        _model = rig.Lens.Model;

        _basis = BuildBasis(rig.BoresightAltDeg, rig.BoresightAzDeg);

        _pitchMm = rig.Sensor.PixelPitchUm > 0 ? rig.Sensor.PixelPitchUm / 1000.0 : 0.0;
        _fMm = Math.Max(0.0, rig.Lens.FocalLengthMm);

        // --- Rectilinear focal lengths in pixels ---
        // Prefer physical focal length when pixel pitch available; otherwise compute from FOV.
        if (_pitchMm > 0.0 && _fMm > 0.0)
        {
            var fpx = _fMm / _pitchMm;     // square pixels assumed
            _fx = fpx;
            _fy = fpx;
        }
        else
        {
            // Fallback from horizontal FOV (deg)
            var fovX = Math.Clamp(rig.Lens.FovXDeg, 1.0, 179.0) * Math.PI / 180.0;
            var halfX = fovX * 0.5;
            _fx = (_w * 0.5) / Math.Tan(halfX);
            // derive fy from aspect
            var halfY = Math.Atan((_h / (double)_w) * Math.Tan(halfX));
            _fy = (_h * 0.5) / Math.Tan(halfY);
        }

        // --- Fisheye f in pixels ---
        // If we have physical focal length + pitch, compute directly; else fit to a circle via FOV.
        if (_pitchMm > 0.0 && _fMm > 0.0)
        {
            _fFisheyePx = _fMm / _pitchMm;
            _thetaMax = Math.Clamp(rig.Lens.FovXDeg, 1.0, 200.0) * Math.PI / 180.0 * 0.5;
            _rMax = Math.Min(_cx, _cy) * horizonPaddingPct; // used only for quick bounds check
        }
        else
        {
            _thetaMax = Math.Clamp(rig.Lens.FovXDeg, 1.0, 200.0) * Math.PI / 180.0 * 0.5;
            _rMax = Math.Min(_cx, _cy) * horizonPaddingPct;
            var g = FisheyeG(_model, _thetaMax); // g(θmax) with f=1
            if (g < 1e-9) g = 1e-9;
            _fFisheyePx = _rMax / g; // scale so the FOV fits the circle
        }
    }

    public bool TryProjectAltAz(double altDeg, double azDeg, out float x, out float y)
    {
        x = y = 0;

        // World ray in ENU (east,north,up):
        var d = DirFromAltAz(altDeg, azDeg);

        // Camera basis (B: forward, E1: right, E2: up-in-image)
        var db  = Clamp(Dot(d, _basis.B), -1.0, 1.0);  // cos(theta)
        var de1 = Dot(d, _basis.E1);
        var de2 = Dot(d, _basis.E2);

        // Rotate by camera roll about optical axis (B)
        var cr = Math.Cos(_rollRad);
        var sr = Math.Sin(_rollRad);
        var xr = cr * de1 - sr * de2;
        var yr = sr * de1 + cr * de2;

        // Culling: for central/perspective, only the front hemisphere (db>0) is visible.
        // For fisheye too, we typically render the forward hemisphere around the boresight.
        if (db <= 0.0) return false;

        if (IsRectilinear(_model))
        {
            // Perspective/Gnomonic pinhole: u = fx * (X/Z), v = fy * (Y/Z)
            var u = _fx * (xr / db);
            var v = _fy * (yr / db);

            var xx = (float)((_flipX ? -u : u) + _cx);
            var yy = (float)(_cy - v);

            if (!InsideSensor(xx, yy)) return false;
            x = xx; y = yy;
            return true;
        }
        else
        {
            // Fisheye family: r(θ) with f in *pixels*
            var theta = Math.Acos(db);           // [0, π]
            var r = _fFisheyePx * FisheyeG(_model, theta); // r = f * g(θ)

            // azimuth in image plane from rotated components
            var phi = Math.Atan2(xr, yr);

            var dx = r * Math.Sin(phi);
            var dy = r * Math.Cos(phi);

            var xx = (float)((_flipX ? -dx : dx) + _cx);
            var yy = (float)(_cy - dy);

            if (!InsideSensor(xx, yy)) return false; // sensor clip
            x = xx; y = yy;
            return true;
        }
    }

    private static bool InsideSensor(float x, float y, int w, int h) =>
        x >= 0 && x < w && y >= 0 && y < h;

    private bool InsideSensor(float x, float y) => InsideSensor(x, y, _w, _h);

    private static double FisheyeG(ProjectionModel model, double theta) => model switch
    {
        ProjectionModel.Equidistant     => theta,                    // r = f * θ
        ProjectionModel.EquisolidAngle  => 2.0 * Math.Sin(theta/2),  // r = 2 f sin(θ/2)
        ProjectionModel.Orthographic    => Math.Sin(theta),          // r = f sin θ
        ProjectionModel.Stereographic   => 2.0 * Math.Tan(theta/2),  // r = 2 f tan(θ/2)
        _ => theta
    };

    private static bool IsRectilinear(ProjectionModel m) =>
        m == ProjectionModel.Perspective || m == ProjectionModel.Gnomonic;

    private static double Clamp(double v, double lo, double hi) =>
        v < lo ? lo : (v > hi ? hi : v);
}
