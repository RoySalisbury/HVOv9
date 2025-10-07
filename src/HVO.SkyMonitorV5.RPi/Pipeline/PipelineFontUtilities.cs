#nullable enable

using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Pipeline;

internal static class PipelineFontUtilities
{
    private static readonly string[] PreferredFamilies =
    {
        "Segoe UI",
        "Arial",
        "Liberation Sans",
        "DejaVu Sans",
        "Inter"
    };

    public static SKTypeface ResolveTypeface(SKFontStyleWeight weight)
    {
        foreach (var family in PreferredFamilies)
        {
            var typeface = SKTypeface.FromFamilyName(family, new SKFontStyle(weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright));
            if (typeface is not null && typeface.FamilyName.Length > 0)
            {
                return typeface;
            }
        }

        return SKTypeface.Default;
    }
}
