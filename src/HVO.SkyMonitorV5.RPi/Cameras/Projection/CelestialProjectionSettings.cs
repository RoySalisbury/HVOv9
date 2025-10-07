#nullable enable

using System;
using HVO.SkyMonitorV5.RPi.Cameras.MockCamera;

namespace HVO.SkyMonitorV5.RPi.Cameras.Projection;

public sealed class CelestialProjectionSettings
{
    public CelestialProjectionSettings(
        int width,
        int height,
        double latitudeDegrees,
        double longitudeDegrees,
        FisheyeModel projection,
        double horizonPaddingPercent,
        double fieldOfViewDegrees,
        bool applyRefraction,
        bool flipHorizontal)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        Width = width;
        Height = height;
        LatitudeDegrees = latitudeDegrees;
        LongitudeDegrees = longitudeDegrees;
        Projection = projection;
        HorizonPaddingPercent = Math.Clamp(horizonPaddingPercent, 0.0, 1.0);
        FieldOfViewDegrees = Math.Clamp(fieldOfViewDegrees, 1.0, 360.0);
        ApplyRefraction = applyRefraction;
        FlipHorizontal = flipHorizontal;
    }

    public int Width { get; }
    public int Height { get; }
    public double LatitudeDegrees { get; }
    public double LongitudeDegrees { get; }
    public FisheyeModel Projection { get; }
    public double HorizonPaddingPercent { get; }
    public double FieldOfViewDegrees { get; }
    public bool ApplyRefraction { get; }
    public bool FlipHorizontal { get; }
}
