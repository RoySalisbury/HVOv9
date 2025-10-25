#nullable enable
using System;
using HVO.Astronomy.CFITSIO.Interop;

namespace HVO.Astronomy.CFITSIO;

/// <summary>
/// Fluent helper for WCS header keywords.
/// </summary>
public sealed class WcsHeaderBuilder
{
  private readonly FitsFile _fits;

  /// <summary>Create a new WCS builder bound to <paramref name="fits"/>.</summary>
  public WcsHeaderBuilder(FitsFile fits) => _fits = fits ?? throw new ArgumentNullException(nameof(fits));

  /// <summary>
  /// Set a simple TAN (gnomonic) WCS using pixel scale (degrees per pixel) and reference pixel/value.
  /// Uses CDELT1/CDELT2 and CTYPE1/2 = RA---TAN / DEC--TAN. Positive CDELT2; CDELT1 often negative for RA.
  /// </summary>
  public WcsHeaderBuilder SetSimpleTan(
      double referenceWorldLongitudeDegrees,
      double referenceWorldLatitudeDegrees,
      double referencePixelX,
      double referencePixelY,
      double degreesPerPixelX,
      double degreesPerPixelY,
      string unitsAxis1 = "deg",
      string unitsAxis2 = "deg")
  {
    _fits.WriteKeyString(FitsCommonKeywords.CTYPE1, "RA---TAN", "WCS projection");
    _fits.WriteKeyString(FitsCommonKeywords.CTYPE2, "DEC--TAN", "WCS projection");
    _fits.WriteKeyDouble(FitsCommonKeywords.CRVAL1, referenceWorldLongitudeDegrees, -1, "Reference world longitude (deg)");
    _fits.WriteKeyDouble(FitsCommonKeywords.CRVAL2, referenceWorldLatitudeDegrees, -1, "Reference world latitude (deg)");
    _fits.WriteKeyDouble(FitsCommonKeywords.CRPIX1, referencePixelX, -1, "Reference pixel X (1-based)");
    _fits.WriteKeyDouble(FitsCommonKeywords.CRPIX2, referencePixelY, -1, "Reference pixel Y (1-based)");
    _fits.WriteKeyDouble(FitsCommonKeywords.CDELT1, degreesPerPixelX, -1, "Degrees per pixel (X)");
    _fits.WriteKeyDouble(FitsCommonKeywords.CDELT2, degreesPerPixelY, -1, "Degrees per pixel (Y)");
    _fits.WriteKeyString(FitsCommonKeywords.CUNIT1, unitsAxis1, "Axis 1 units");
    _fits.WriteKeyString(FitsCommonKeywords.CUNIT2, unitsAxis2, "Axis 2 units");
    return this;
  }

  /// <summary>
  /// Set a TAN WCS using a 2x2 CD matrix (degrees per pixel with rotation/shear).
  /// </summary>
  public WcsHeaderBuilder SetTanWithCdMatrix(
      double referenceWorldLongitudeDegrees,
      double referenceWorldLatitudeDegrees,
      double referencePixelX,
      double referencePixelY,
      double cd11, double cd12, double cd21, double cd22,
      string unitsAxis1 = "deg",
      string unitsAxis2 = "deg")
  {
    _fits.WriteKeyString(FitsCommonKeywords.CTYPE1, "RA---TAN", "WCS projection");
    _fits.WriteKeyString(FitsCommonKeywords.CTYPE2, "DEC--TAN", "WCS projection");
    _fits.WriteKeyDouble(FitsCommonKeywords.CRVAL1, referenceWorldLongitudeDegrees, 15, "Reference world longitude (deg)");
    _fits.WriteKeyDouble(FitsCommonKeywords.CRVAL2, referenceWorldLatitudeDegrees, 15, "Reference world latitude (deg)");
    _fits.WriteKeyDouble(FitsCommonKeywords.CRPIX1, referencePixelX, 15, "Reference pixel X (1-based)");
    _fits.WriteKeyDouble(FitsCommonKeywords.CRPIX2, referencePixelY, 15, "Reference pixel Y (1-based)");
    _fits.WriteKeyDouble(FitsCommonKeywords.CD1_1, cd11, 15, "CD matrix 1,1 (deg/pix)");
    _fits.WriteKeyDouble(FitsCommonKeywords.CD1_2, cd12, 15, "CD matrix 1,2 (deg/pix)");
    _fits.WriteKeyDouble(FitsCommonKeywords.CD2_1, cd21, 15, "CD matrix 2,1 (deg/pix)");
    _fits.WriteKeyDouble(FitsCommonKeywords.CD2_2, cd22, 15, "CD matrix 2,2 (deg/pix)");
    _fits.WriteKeyString(FitsCommonKeywords.CUNIT1, unitsAxis1, "Axis 1 units");
    _fits.WriteKeyString(FitsCommonKeywords.CUNIT2, unitsAxis2, "Axis 2 units");
    return this;
  }

  /// <summary>
  /// Optional celestial system details (reference frame, equinox, pole definitions).
  /// </summary>
  public WcsHeaderBuilder SetCelestialSystem(string referenceFrame = "ICRS", double? equinox = null, double? longitudePole = null, double? latitudePole = null)
  {
    if (!string.IsNullOrWhiteSpace(referenceFrame))
      _fits.WriteKeyString(FitsCommonKeywords.RADESYS, referenceFrame, "Celestial reference frame");

    if (equinox.HasValue)
      _fits.WriteKeyDouble(FitsCommonKeywords.EQUINOX, equinox.Value, -1, "Equinox (year)");

    if (longitudePole.HasValue)
      _fits.WriteKeyDouble(FitsCommonKeywords.LONPOLE, longitudePole.Value, -1, "Native longitude of celestial pole");

    if (latitudePole.HasValue)
      _fits.WriteKeyDouble(FitsCommonKeywords.LATPOLE, latitudePole.Value, -1, "Native latitude of celestial pole");

    return this;
  }
}
