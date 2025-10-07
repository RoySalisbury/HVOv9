#nullable enable

using System;
using HVO.Astronomy;
using HVO.SkyMonitorV5.RPi.Cameras.MockCamera;

namespace HVO.SkyMonitorV5.RPi.Cameras.Projection;

public sealed class CelestialProjectionContext
{
    private readonly double _lstHours;
    private readonly double _latitudeDeg;
    private readonly double _longitudeDeg;
    private readonly FisheyeModel _projection;
    private readonly bool _applyRefraction;
    private readonly bool _flipHorizontal;
    private readonly double _fovDeg;

    internal CelestialProjectionContext(CelestialProjectionSettings settings, DateTime utc)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Utc = utc.ToUniversalTime();

        _latitudeDeg = settings.LatitudeDegrees;
        _longitudeDeg = settings.LongitudeDegrees;
        _projection = settings.Projection;
        _applyRefraction = settings.ApplyRefraction;
        _flipHorizontal = settings.FlipHorizontal;
        _fovDeg = settings.FieldOfViewDegrees;

        CenterX = settings.Width * 0.5f;
        CenterY = settings.Height * 0.5f;
        MaxRadius = (float)(Math.Min(CenterX, CenterY) * settings.HorizonPaddingPercent);

        _lstHours = AstronomyMath.LocalSiderealTime(Utc, _longitudeDeg);
    }

    public CelestialProjectionSettings Settings { get; }
    public DateTime Utc { get; }
    public float CenterX { get; }
    public float CenterY { get; }
    public float MaxRadius { get; }
    public double LocalSiderealTimeHours => _lstHours;

    public bool TryProjectStar(Star star, out float x, out float y)
    {
        if (star is null)
        {
            x = 0f;
            y = 0f;
            return false;
        }

        return TryProjectEquatorial(star.RightAscensionHours, star.DeclinationDegrees, out x, out y);
    }

    public bool TryProjectEquatorial(double raHours, double decDegrees, out float x, out float y)
    {
        var (altitudeDeg, azimuthDeg) = AstronomyMath.EquatorialToHorizontal(raHours, decDegrees, _lstHours, _latitudeDeg);

        if (_applyRefraction)
        {
            altitudeDeg += AstronomyMath.BennettRefractionDegrees(altitudeDeg);
        }

        if (altitudeDeg < 0.0)
        {
            x = 0f;
            y = 0f;
            return false;
        }

        return TryProjectHorizontal(altitudeDeg, azimuthDeg, out x, out y);
    }

    public bool TryProjectHorizontal(double altitudeDeg, double azimuthDeg, out float x, out float y)
    {
        x = 0f;
        y = 0f;

        var theta = AstronomyMath.DegreesToRadians(90.0 - altitudeDeg);
        if (theta < 0)
        {
            return false;
        }

        var thetaMax = Math.PI * (_fovDeg / 360.0);
        theta = Math.Min(theta, thetaMax);

        double rPrime = _projection switch
        {
            FisheyeModel.Equidistant => theta / thetaMax,
            FisheyeModel.EquisolidAngle => Math.Sin(theta / 2.0) / Math.Sin(thetaMax / 2.0),
            FisheyeModel.Orthographic => Math.Sin(theta) / Math.Sin(thetaMax),
            FisheyeModel.Stereographic => Math.Tan(theta / 2.0) / Math.Tan(thetaMax / 2.0),
            _ => theta / thetaMax
        };

        rPrime = Math.Min(rPrime, 1.0);
        var radius = (float)(rPrime * MaxRadius);
        var azimuthRad = AstronomyMath.DegreesToRadians(azimuthDeg);

        var horizontalOffset = (float)(radius * Math.Sin(azimuthRad));
        x = _flipHorizontal ? CenterX - horizontalOffset : CenterX + horizontalOffset;
        y = CenterY - (float)(radius * Math.Cos(azimuthRad));

        var dx = x - CenterX;
        var dy = y - CenterY;
        return dx * dx + dy * dy <= MaxRadius * MaxRadius + 1.0f;
    }
}
