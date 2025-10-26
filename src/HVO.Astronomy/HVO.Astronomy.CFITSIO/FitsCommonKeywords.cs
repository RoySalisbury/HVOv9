#nullable enable
using System;

namespace HVO.Astronomy.CFITSIO;

/// <summary>
/// Common FITS keyword names and small helpers.
/// </summary>
public static class FitsCommonKeywords
{
  // Structural
  public const string SIMPLE = "SIMPLE";
  public const string BITPIX = "BITPIX";
  public const string NAXIS = "NAXIS";
  public const string NAXIS1 = "NAXIS1";
  public const string NAXIS2 = "NAXIS2";
  public const string EXTEND = "EXTEND";
  public const string BSCALE = "BSCALE";
  public const string BZERO = "BZERO";
  public const string BUNIT = "BUNIT";

  // Timing/observation
  public const string DATE = "DATE";
  public const string DATEOBS = "DATE-OBS";
  public const string MJDOBS = "MJD-OBS";
  public const string EXPTIME = "EXPTIME";

  // WCS — 2D subset
  public const string CTYPE1 = "CTYPE1";
  public const string CTYPE2 = "CTYPE2";
  public const string CRVAL1 = "CRVAL1";
  public const string CRVAL2 = "CRVAL2";
  public const string CRPIX1 = "CRPIX1";
  public const string CRPIX2 = "CRPIX2";
  public const string CDELT1 = "CDELT1";
  public const string CDELT2 = "CDELT2";
  public const string CUNIT1 = "CUNIT1";
  public const string CUNIT2 = "CUNIT2";
  public const string CD1_1 = "CD1_1";
  public const string CD1_2 = "CD1_2";
  public const string CD2_1 = "CD2_1";
  public const string CD2_2 = "CD2_2";
  public const string LONPOLE = "LONPOLE";
  public const string LATPOLE = "LATPOLE";
  public const string EQUINOX = "EQUINOX";
  public const string RADESYS = "RADESYS";

  /// <summary>Return ISO-8601 timestamp suitable for DATE-OBS.</summary>
  public static string IsoTimestamp(DateTime utc) =>
      utc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ");
}
