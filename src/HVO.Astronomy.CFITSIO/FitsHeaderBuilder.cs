#nullable enable
using System;
using HVO.Astronomy.CFITSIO.Interop;

namespace HVO.Astronomy.CFITSIO;

/// <summary>
/// Fluent helper for stamping common header keywords.
/// </summary>
public sealed class FitsHeaderBuilder
{
  private readonly FitsFile _fits;

  /// <summary>Create a new header builder bound to <paramref name="fits"/>.</summary>
  public FitsHeaderBuilder(FitsFile fits) => _fits = fits ?? throw new ArgumentNullException(nameof(fits));

  /// <summary>Set an arbitrary string keyword.</summary>
  public FitsHeaderBuilder SetString(string keyword, string value, string comment = "")
  {
    _fits.WriteKeyString(keyword, value, comment);
    return this;
  }

  /// <summary>Set a 32-bit integer keyword.</summary>
  public FitsHeaderBuilder SetInt32(string keyword, int value, string comment = "")
  {
    _fits.WriteKeyInt32(keyword, value, comment);
    return this;
  }

  /// <summary>Set a 64-bit integer keyword.</summary>
  public FitsHeaderBuilder SetInt64(string keyword, long value, string comment = "")
  {
    _fits.WriteKeyInt64(keyword, value, comment);
    return this;
  }

  /// <summary>Set a double-precision keyword.</summary>
  public FitsHeaderBuilder SetDouble(string keyword, double value, int decimals = -1, string comment = "")
  {
    _fits.WriteKeyDouble(keyword, value, decimals, comment);
    return this;
  }

  /// <summary>Set a logical (boolean) keyword.</summary>
  public FitsHeaderBuilder SetBoolean(string keyword, bool value, string comment = "")
  {
    _fits.WriteKeyBoolean(keyword, value, comment);
    return this;
  }

  /// <summary>Stamp DATE with current UTC.</summary>
  public FitsHeaderBuilder StampCurrentDate() { int s = 0; CFitsIO.fits_write_date(_fits.Handle, ref s); CFitsIO.ThrowIfError(s); return this; }

  /// <summary>Set DATE-OBS in ISO-8601 from a UTC timestamp.</summary>
  public FitsHeaderBuilder SetDateObs(DateTime utc)
  {
    _fits.WriteKeyString(FitsCommonKeywords.DATEOBS, FitsCommonKeywords.IsoTimestamp(utc), "Start of observation (UTC)");
    return this;
  }

  /// <summary>Set exposure time in seconds.</summary>
  public FitsHeaderBuilder SetExposureSeconds(double seconds)
  {
    _fits.WriteKeyDouble(FitsCommonKeywords.EXPTIME, seconds, -1, "Exposure time (s)");
    return this;
  }

  /// <summary>Set BSCALE/BZERO explicitly.</summary>
  public FitsHeaderBuilder SetScale(double bScale, double bZero)
  {
    _fits.SetScale(bScale, bZero);
    return this;
  }
}
