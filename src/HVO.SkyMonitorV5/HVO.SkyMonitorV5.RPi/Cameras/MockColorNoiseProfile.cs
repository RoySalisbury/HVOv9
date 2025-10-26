#nullable enable
using System;

namespace HVO.SkyMonitorV5.RPi.Cameras;

/// <summary>
/// Shapes the chroma noise and twinkle adjustments applied by <see cref="MockColorCameraAdapter"/>.
/// </summary>
public sealed class MockColorNoiseProfile
{
    public static MockColorNoiseProfile Default { get; } = new MockColorNoiseProfile(
        chromaNoiseScale: 1.0d,
        greenChromaCompensationFactor: 0.35d,
        greenTwinkleScale: 0.5d);

    public MockColorNoiseProfile(
        double chromaNoiseScale,
        double greenChromaCompensationFactor,
        double greenTwinkleScale)
    {
        if (chromaNoiseScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chromaNoiseScale), chromaNoiseScale, "Chroma noise scale must be positive.");
        }

        if (greenChromaCompensationFactor < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(greenChromaCompensationFactor), greenChromaCompensationFactor, "Compensation factor must be non-negative.");
        }

        if (greenTwinkleScale < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(greenTwinkleScale), greenTwinkleScale, "Twinkle scale must be non-negative.");
        }

        ChromaNoiseScale = chromaNoiseScale;
        GreenChromaCompensationFactor = greenChromaCompensationFactor;
        GreenTwinkleScale = greenTwinkleScale;
    }

    public double ChromaNoiseScale { get; }

    public double GreenChromaCompensationFactor { get; }

    public double GreenTwinkleScale { get; }
}
