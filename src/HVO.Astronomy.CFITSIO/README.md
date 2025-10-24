
# Cfitsio.Native
Native CFITSIO binaries for multi-RID .NET apps.

Place your `[DllImport("cfitsio")]` in code; the .NET loader resolves from this package's `runtimes/**/native` folders.


// Compress file (Rice 32x32), write checksums
using var f = FitsFile.OpenRead("raw.fits");
f.CompressTo("!raw_compressed.fits", FitsCompression.Rice32x32, overwrite: true);

// Policy-driven recompress (keep only if ≥15% smaller and final ≤ 500 MB)
var keptPath = f.CompressToWithPolicy(
    outputPath: "raw_best.fits",
    compression: FitsCompression.Rice64x64,
    policy: new FitsFile.CompressionPolicy { MinRelativeGain = 0.15, MaxOutputMegabytes = 500, MinInputMegabytes = 10 },
    overwrite: true
);
Console.WriteLine($"Kept: {keptPath}");

// FITS → PNG (auto: 16-bit RGBA if needed, else 8-bit)
var img = f.GetCurrentImage();
img.Save("frame.png");

// FITS → JPEG with stretch
img.Save("frame.jpg", jpegQuality: 92);

// JPEG → FITS U16
FitsImage.CreateFromImageU16("snapshot.jpg", "!snapshot.fits", overwrite: true);


-----------

using HVO.Astronomy.CFITSIO;
using HVO.Astronomy.CFITSIO.Skia;
using HVO.Astronomy.CFITSIO.Wcs;
using SkiaSharp;

// SKImage → FITS (mono U16) with Rice compression
using var img = SKImage.FromEncodedData("frame.png");
img.SaveAsFitsU16("!frame.fits", overwrite: true, compression: FitsCompression.Rice32x32, stampHeader: k =>
{
    k.Set("EXPTIME", 120.0, 3, "Exposure time (s)");
    k.Set("INSTRUME", "ASI174MM");
    k.SetUtcDateObs(DateTimeOffset.UtcNow);

    // TAN WCS
    k.SetTan(new FitsWcs.TanWcs(
        Crpix1: 1024.5, Crpix2: 768.5,
        Crval1Deg: 210.123456, Crval2Deg: -2.345678,
        Cd1_1: -2.5 / 3600.0, Cd1_2: 0.0,
        Cd2_1: 0.0, Cd2_2: 2.5 / 3600.0,
        Radesys: "ICRS", Equinox: 2000.0));
});

// FITS → Skia preview & smart save (auto 16-bit PNG if needed)
using var f = FitsFile.OpenRead("frame.fits");
var fi = f.GetCurrentImage();
fi.Save("preview.png");        // Auto: 8-bit gray or 16-bit RGBA
fi.Save("preview.jpg", jpegQuality: 90);

// Recompress an existing FITS by policy (≥15% reduction)
var kept = new FileInfo("frame.fits").RecompressFits(
    FitsCompression.Rice64x64,
    new FitsFile.CompressionPolicy { MinRelativeGain = 0.15, MinInputMegabytes = 1.0 },
    outputPath: "frame_cmp.fits",
    overwrite: true);
Console.WriteLine($"Kept: {kept}");

// Read WCS back
if (f.Keywords.TryGetTan(out var tan))
{
    Console.WriteLine($"CRVAL1/2: {tan.Crval1Deg}, {tan.Crval2Deg} deg");
}

Notes & tips

Color handling: FITS is typically mono or multi-plane. The SaveAsFitsRgbU16 helper writes a 3-plane cube (R,G,B) with U16 planes; many astro tools are mono-first, but the cube pattern is standard.

WCS math: The helper doesn’t do coordinate transforms; for that you’d normally bind WCSLIB. This file just makes authoring/reading the header clean and typesafe.

SIP terms: If you need distortion polynomials, add typed setters/getters for A_i_j, B_i_j, and their inverse AP_i_j, BP_i_j. The keyword naming is mechanical, so it’s easy to extend from the SetSipOrder() stub.

Compression: For integer images, Rice is lossless and fast. For float, consider quantized lossy (fpack-style) presets later.

Smart PNG depth: Your existing FitsImage.Save() already switches between 8-bit gray and 16-bit-per-channel RGBA PNG based on content.


-----------

#nullable enable
using System;

namespace HVO.Astronomy.CFITSIO.Wcs
{
    /// <summary>
    /// Builder for a minimal TAN WCS using pixel scale and rotation.
    /// Produces a <see cref="FitsWcs.TanWcs"/> (CRVAL/CRPIX + CD matrix) you can stamp with <c>FitsWcs.SetTan()</c>.
    /// </summary>
    public sealed class FitsWcsBuilder
    {
        /// <summary>Reference sky position at the reference pixel (deg).</summary>
        public double Crval1Deg { get; private set; }  // RA
        public double Crval2Deg { get; private set; }  // DEC

        /// <summary>Reference pixel (FITS 1-based).</summary>
        public double Crpix1 { get; private set; } = 1.0;
        public double Crpix2 { get; private set; } = 1.0;

        /// <summary>Pixel scale along X and Y (arcsec/pixel). Use same value for square pixels.</summary>
        public double ScaleXArcsecPerPix { get; private set; } = 1.0;
        public double ScaleYArcsecPerPix { get; private set; } = 1.0;

        /// <summary>
        /// Rotation definition for mapping image axes to the sky.
        /// </summary>
        public enum RotationConvention
        {
            /// <summary>
            /// θ is the angle from +Y pixel axis to +North (direction of increasing DEC),
            /// measured counterclockwise on the image. θ = 0 ⇒ +Y points to North.
            /// This is the most common “position angle” used in astro imaging UIs.
            /// </summary>
            PositionAngleOfYToNorth,

            /// <summary>
            /// θ is the angle from +X pixel axis to +East (direction of increasing longitude on the sky),
            /// measured counterclockwise. Note: on the sky, +East corresponds to **decreasing RA** at fixed DEC.
            /// </summary>
            AngleOfXToEast
        }

        /// <summary>
        /// By FITS/WCS convention, the CD matrix usually carries a negative sign in the first row
        /// so that +X to the right corresponds to **West** on the sky (RA decreasing to the right).
        /// Keep this true unless you know your pipeline wants the alternative.
        /// </summary>
        public bool ApplyRaFlipInCd { get; private set; } = true;

        /// <summary>Rotation angle θ in degrees according to <see cref="Convention"/>.</summary>
        public double RotationDeg { get; private set; } = 0.0;

        /// <summary>Rotation convention.</summary>
        public RotationConvention Convention { get; private set; } = RotationConvention.PositionAngleOfYToNorth;

        /// <summary>Optional: RADESYS (e.g., ICRS, FK5).</summary>
        public string Radesys { get; private set; } = "ICRS";

        /// <summary>Optional: EQUINOX for non-ICRS frames (e.g., 2000.0 for FK5).</summary>
        public double? Equinox { get; private set; }

        // ── Fluent API ─────────────────────────────────────────────────────────

        public FitsWcsBuilder SetReferenceSky(double raDeg, double decDeg)
        { Crval1Deg = raDeg; Crval2Deg = decDeg; return this; }

        /// <summary>Set reference pixel (FITS 1-based coordinates).</summary>
        public FitsWcsBuilder SetReferencePixel(double crpix1, double crpix2)
        { Crpix1 = crpix1; Crpix2 = crpix2; return this; }

        /// <summary>Set square pixel scale in arcsec/pixel.</summary>
        public FitsWcsBuilder SetPixelScale(double arcsecPerPixel)
        { ScaleXArcsecPerPix = ScaleYArcsecPerPix = arcsecPerPixel; return this; }

        /// <summary>Set pixel scales (possibly non-square) in arcsec/pixel.</summary>
        public FitsWcsBuilder SetPixelScales(double xArcsecPerPixel, double yArcsecPerPixel)
        { ScaleXArcsecPerPix = xArcsecPerPixel; ScaleYArcsecPerPix = yArcsecPerPixel; return this; }

        /// <summary>Set rotation θ and convention.</summary>
        public FitsWcsBuilder SetRotation(double rotationDeg, RotationConvention convention = RotationConvention.PositionAngleOfYToNorth)
        { RotationDeg = rotationDeg; Convention = convention; return this; }

        /// <summary>Use or skip the usual RA sign flip in the CD matrix first row.</summary>
        public FitsWcsBuilder SetRaFlip(bool apply) { ApplyRaFlipInCd = apply; return this; }

        public FitsWcsBuilder SetFrame(string radesys = "ICRS", double? equinox = null)
        { Radesys = radesys; Equinox = equinox; return this; }

        // ── Build / Apply ──────────────────────────────────────────────────────

        /// <summary>
        /// Build a <see cref="FitsWcs.TanWcs"/> (CRVAL/CRPIX/CD) with your settings.
        /// </summary>
        public FitsWcs.TanWcs Build()
        {
            // Convert scales to degrees/pixel
            double sx = ScaleXArcsecPerPix / 3600.0;
            double sy = ScaleYArcsecPerPix / 3600.0;

            // We need CD1_1, CD1_2, CD2_1, CD2_2 in deg/pix.
            // Start with a rotation defined per convention, then inject the RA flip (negative sign) if requested.
            double thetaRad = DegreesToRadians(RotationDeg);

            // We will compute how image +X/+Y map to sky coordinates (ξ, η) in degrees on the tangent plane,
            // where the final CD rows correspond to [dRA*cos(DEC), dDEC].
            // Common, practical mapping with PositionAngleOfYToNorth (θ=0 ⇒ +Y to North):
            //   CD = [ [-sx * cosθ,  +sy * sinθ],
            //          [ +sx * sinθ,  +sy * cosθ] ]      (RA flip baked into first row)
            //
            // For the alternative convention (AngleOfXToEast; θ=0 ⇒ +X to East),
            // a consistent mapping with RA flip is:
            //   CD = [ [-sx * cosθ,  -sy * sinθ],
            //          [ +sx * sinθ,  -sy * cosθ] ].
            //
            // These choices preserve: +Y up → North at θ=0; +X right → West at θ=0 if ApplyRaFlipInCd=true.

            double c = Math.Cos(thetaRad);
            double s = Math.Sin(thetaRad);

            double cd11, cd12, cd21, cd22;

            if (Convention == RotationConvention.PositionAngleOfYToNorth)
            {
                cd11 = (ApplyRaFlipInCd ? -1 : +1) * sx * c;
                cd12 = +sy * s;
                cd21 = +sx * s;
                cd22 = +sy * c;
            }
            else // AngleOfXToEast
            {
                cd11 = (ApplyRaFlipInCd ? -1 : +1) * sx * c;
                cd12 = (ApplyRaFlipInCd ? -1 : +1) * sy * s;
                cd21 = +sx * s;
                cd22 = (ApplyRaFlipInCd ? -1 : +1) * sy * c;
            }

            return new FitsWcs.TanWcs(
                Crpix1: Crpix1,
                Crpix2: Crpix2,
                Crval1Deg: Crval1Deg,
                Crval2Deg: Crval2Deg,
                Cd1_1: cd11,
                Cd1_2: cd12,
                Cd2_1: cd21,
                Cd2_2: cd22,
                Radesys: Radesys,
                Equinox: Equinox
            );
        }

        /// <summary>
        /// Build and immediately stamp the keywords on the current FITS HDU (uses <see cref="FitsWcs.SetTan"/>).
        /// </summary>
        public void ApplyTo(FitsFile.FitsKeywords header)
        {
            var tan = Build();
            header.SetTan(tan);
        }

        // ── Convenience constructors ───────────────────────────────────────────

        /// <summary>
        /// Create a builder for a square-pixel TAN WCS with a given position angle (θ) of +Y to North (deg).
        /// </summary>
        public static FitsWcsBuilder TanSquare(double raDeg, double decDeg, double crpix1, double crpix2,
                                               double arcsecPerPixel, double thetaDeg_PosYToNorth,
                                               bool raFlip = true, string radesys = "ICRS", double? equinox = null)
            => new FitsWcsBuilder()
                .SetReferenceSky(raDeg, decDeg)
                .SetReferencePixel(crpix1, crpix2)
                .SetPixelScale(arcsecPerPixel)
                .SetRotation(thetaDeg_PosYToNorth, RotationConvention.PositionAngleOfYToNorth)
                .SetRaFlip(raFlip)
                .SetFrame(radesys, equinox);

        /// <summary>
        /// Create a builder from CDELT/CROTA2 legacy parameters (square pixels).
        /// CROTA2 is the rotation of the latitude axis; 0 ⇒ North up.
        /// </summary>
        public static FitsWcsBuilder FromCdeltCrota(double raDeg, double decDeg, double crpix1, double crpix2,
                                                    double cdelt1DegPerPix, double cdelt2DegPerPix, double crota2Deg,
                                                    string radesys = "ICRS", double? equinox = null)
        {
            // Convert CDELT to arcsec/pixel (magnitudes), rotation θ = CROTA2, RA flip from sign of CDELT1
            double sx = Math.Abs(cdelt1DegPerPix) * 3600.0;
            double sy = Math.Abs(cdelt2DegPerPix) * 3600.0;
            bool raFlip = cdelt1DegPerPix < 0; // typical headers have CDELT1 < 0

            return new FitsWcsBuilder()
                .SetReferenceSky(raDeg, decDeg)
                .SetReferencePixel(crpix1, crpix2)
                .SetPixelScales(sx, sy)
                .SetRotation(crota2Deg, RotationConvention.PositionAngleOfYToNorth)
                .SetRaFlip(raFlip)
                .SetFrame(radesys, equinox);
        }

        private static double DegreesToRadians(double d) => d * Math.PI / 180.0;
    }
}

Notes on conventions (important!)

RA flip (ApplyRaFlipInCd): by default we set CD1_* negative so +X right = West (RA decreases to the right). That matches most astro tools and headers (e.g., CDELT1 < 0). If you want +X right = East, set SetRaFlip(false).

Rotation angle:

PositionAngleOfYToNorth — θ measured CCW from +Y to +North. θ=0 → +Y is North; θ increases through East.

AngleOfXToEast — θ measured CCW from +X to +East.

Square vs non-square pixels: use SetPixelScale for square, or SetPixelScales(x,y) for anamorphic sensors.