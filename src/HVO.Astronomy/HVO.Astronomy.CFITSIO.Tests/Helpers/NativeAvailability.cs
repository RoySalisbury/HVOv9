using System;
using HVO.Astronomy.CFITSIO.Interop;

namespace HVO.Astronomy.CFITSIO.Tests.Helpers;

internal static class NativeAvailability
{
  private static bool? _has;
  public static bool HasCFitsIO
  {
    get
    {
      if (_has.HasValue) return _has.Value;
      try
      {
        double v;
        CFitsIO.fits_get_version(out v);
        _has = v > 0;
      }
      catch
      {
        _has = false;
      }
      return _has.Value;
    }
  }
}
