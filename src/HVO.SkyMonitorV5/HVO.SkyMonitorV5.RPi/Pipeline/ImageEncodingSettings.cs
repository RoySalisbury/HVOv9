namespace HVO.SkyMonitorV5.RPi.Pipeline;

/// <summary>
/// Represents the runtime image encoding settings applied to processed frames.
/// </summary>
public sealed record ImageEncodingSettings
{
    public ImageEncodingFormat Format { get; init; }

    public int Quality { get; init; }

    /// <summary>
    /// FITS-specific encoding options. Only used when Format is Fits.
    /// </summary>
    public FitsEncodingOptions? FitsOptions { get; init; }

    public ImageEncodingSettings(ImageEncodingFormat format, int quality, FitsEncodingOptions? fitsOptions = null)
    {
        Format = format;
        Quality = quality;
        FitsOptions = fitsOptions;
    }

    public ImageEncodingSettings() : this(ImageEncodingFormat.Jpeg, 90, null)
    {
    }
}
