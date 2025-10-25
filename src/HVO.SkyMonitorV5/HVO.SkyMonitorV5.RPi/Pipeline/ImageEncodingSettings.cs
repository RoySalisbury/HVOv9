namespace HVO.SkyMonitorV5.RPi.Pipeline;

/// <summary>
/// Represents the runtime image encoding settings applied to processed frames.
/// </summary>
public sealed record ImageEncodingSettings
{
    public ImageEncodingFormat Format { get; init; }

    public int Quality { get; init; }

    public ImageEncodingSettings(ImageEncodingFormat format, int quality)
    {
        Format = format;
        Quality = quality;
    }

    public ImageEncodingSettings() : this(ImageEncodingFormat.Jpeg, 90)
    {
    }
}
