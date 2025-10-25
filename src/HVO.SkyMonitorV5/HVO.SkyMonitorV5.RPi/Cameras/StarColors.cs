#nullable enable
using SkiaSharp;

/// <summary>
/// Global StarColors helper so call sites can reference <c>StarColors</c> without additional using directives.
/// </summary>
public static class StarColors
{
    public static SKColor FromCatalog(string? spectral, double? colorIndexBV)
        => (colorIndexBV is double bv && !double.IsNaN(bv)) ? FromBV(bv) : SpectralColor(spectral);

    public static SKColor SpectralColor(string? spectral)
    {
        if (string.IsNullOrWhiteSpace(spectral)) return SKColors.White;
        char c = char.ToUpperInvariant(spectral.Trim()[0]);
        return c switch
        {
            'O' => new SKColor(155, 176, 255),
            'B' => new SKColor(170, 191, 255),
            'A' => new SKColor(202, 215, 255),
            'F' => new SKColor(248, 247, 255),
            'G' => new SKColor(255, 244, 234),
            'K' => new SKColor(255, 210, 161),
            'M' => new SKColor(255, 180, 140),
            _   => SKColors.White
        };
    }

    public static SKColor FromBV(double bv)
    {
        bv = Math.Clamp(bv, -0.4, 2.0);
        double r, g, b;

        if (bv < 0.0)       r = 0.61 + 0.11 * bv + 0.1 * bv * bv;
        else if (bv < 0.4)  r = 0.83 + 0.17 * bv;
        else if (bv < 1.6)  r = 1.00;
        else                r = 1.00;

        if (bv < 0.0)       g = 0.70 + 0.07 * bv + 0.1 * bv * bv;
        else if (bv < 0.4)  g = 0.87 + 0.11 * bv;
        else if (bv < 1.5)  g = 0.98 - 0.16 * (bv - 0.4);
        else                g = 0.82 - 0.5  * (bv - 1.5);

        if (bv < 0.4)       b = 1.00;
        else if (bv < 1.5)  b = 1.00 - 0.47 * (bv - 0.4);
        else                b = 0.63 - 0.6  * (bv - 1.5);

        byte R = (byte)Math.Clamp((int)Math.Round(r * 255), 0, 255);
        byte G = (byte)Math.Clamp((int)Math.Round(g * 255), 0, 255);
        byte B = (byte)Math.Clamp((int)Math.Round(b * 255), 0, 255);
        return new SKColor(R, G, B);
    }
}
