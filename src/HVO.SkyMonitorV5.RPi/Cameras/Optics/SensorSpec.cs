#nullable enable
using System;

namespace HVO.SkyMonitorV5.RPi.Cameras.Optics
{
    /// <summary>
    /// Sensor model (pixel dimensions and pitch). Provides principal point Cx/Cy defaults to image center.
    /// Includes aliases so older call-sites using <c>PixelSizeMicrons</c> and Cx/Cy expectancies continue to compile.
    /// </summary>
    public sealed record SensorSpec(
        int WidthPx,
        int HeightPx,
        double PixelSizeMicrons,   // keep this parameter name to match existing named-arg call sites
        double? CxPx = null,
        double? CyPx = null)
    {
        /// <summary>Pixel pitch in µm (alias of PixelSizeMicrons).</summary>
        public double PixelPitchUm => PixelSizeMicrons;

        /// <summary>Principal point X (px). Defaults to center if null supplied.</summary>
        public double Cx => CxPx ?? (WidthPx * 0.5);
        /// <summary>Principal point Y (px). Defaults to center if null supplied.</summary>
        public double Cy => CyPx ?? (HeightPx * 0.5);
    }
}
