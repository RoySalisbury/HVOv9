using HVO.Astronomy;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Simulation;

/// <summary>
/// Provides shared helpers for synthetic sky rendering.
/// </summary>
internal static class SkySimulationMath
{

    /// <summary>
    /// Offset applied to the vertical center so that rotation pivots near Polaris.
    /// </summary>
    public const float RotationCenterYOffset = -0.12f;

    /// <summary>
    /// Calculates the rotation angle (in degrees) of the sky dome for the provided timestamp.
    /// A negative rotation corresponds to the apparent clockwise motion of the star field.
    /// </summary>
    public static float CalculateSkyRotationDegrees(DateTimeOffset timestamp, double longitudeDegrees)
        => AstronomyMath.CalculateSkyRotationDegrees(timestamp, longitudeDegrees);

    public static float CalculateSkyRotationDegrees(DateTimeOffset timestamp)
        => CalculateSkyRotationDegrees(timestamp, 0d);

    public static SKPoint GetSkyCenter(int width, int height, double latitudeDegrees)
    {
        var offset = CalculateRotationCenterYOffset(latitudeDegrees);
        var centerX = width / 2f;
        var centerY = height / 2f + height * offset;
        return new SKPoint(centerX, centerY);
    }

    public static SKPoint GetSkyCenter(int width, int height)
    {
        var centerX = width / 2f;
        var centerY = height / 2f + height * RotationCenterYOffset;
        return new SKPoint(centerX, centerY);
    }

    public static float CalculateRotationCenterYOffset(double latitudeDegrees)
    {
        var clampedLatitude = Math.Clamp(latitudeDegrees, -90d, 90d);
        return (float)(-clampedLatitude / 290d);
    }

    public static float GetSkyRadius(int width, int height)
    {
        var halfMin = Math.Min(width, height) / 2f;
        return halfMin * 0.96f;
    }
}
