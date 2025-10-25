using System;
using System.IO;
using FluentAssertions;
using HVO.Astronomy.CFITSIO.Interop;
using HVO.Astronomy.CFITSIO.Tests.Helpers;

namespace HVO.Astronomy.CFITSIO.Tests;

[TestClass]
public class HeaderAndWcsTests
{
  [TestMethod]
  public void HeaderBuilder_Writes_Typed_Keywords()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");
    string path = TestPaths.GetTempFile($"headers_{Guid.NewGuid():N}.fits");
    TestPaths.DeleteIfExists(path);

    var created = FitsFile.Create("!" + path);
    created.IsSuccessful.Should().BeTrue(created.Error?.ToString());
    using var fits = created.Value;

    var rimg = fits.CreateImageHdu(CFitsIO.USHORT_IMG, 3, 3);
    rimg.IsSuccessful.Should().BeTrue(rimg.Error?.ToString());

    var hb = new FitsHeaderBuilder(fits)
      .SetString("OBSERVER", "test-user", "Observer name")
      .SetInt32("SEQ", 42, "Sequence")
      .SetInt64("EPOCH_MS", 1234567890123L, "Epoch in ms")
      .SetDouble("GAIN", 1.5, 2, "Gain (e-/ADU)")
      .SetBoolean("FOCUS_OK", true, "Focus status")
  .StampCurrentDate()
  .SetDateObs(DateTime.UtcNow)
      .SetExposureSeconds(3.2)
      .SetScale(1.0, 32768.0);

    // Validate a few keys
    fits.TryGetKeyString("OBSERVER").Value.Should().Be("test-user");
    fits.TryGetKeyInt32("SEQ").Value.Should().Be(42);
    fits.TryGetKeyInt64("EPOCH_MS").Value.Should().Be(1234567890123L);
    fits.TryGetKeyDouble("GAIN").Value.Should().BeApproximately(1.5, 1e-9);
    fits.TryGetKeyBoolean("FOCUS_OK").Value.Should().BeTrue();
    fits.TryGetKeyDouble(FitsCommonKeywords.BSCALE).Value.Should().Be(1.0);
    fits.TryGetKeyDouble(FitsCommonKeywords.BZERO).Value.Should().Be(32768.0);

    fits.Dispose();
    TestPaths.DeleteIfExists(path);
  }

  [TestMethod]
  public void WcsBuilder_Writes_TAN_With_CD_Matrix()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");
    string path = TestPaths.GetTempFile($"wcs_{Guid.NewGuid():N}.fits");
    TestPaths.DeleteIfExists(path);

    var created = FitsFile.Create("!" + path);
    created.IsSuccessful.Should().BeTrue(created.Error?.ToString());
    using var fits = created.Value;

    var rimg = fits.CreateImageHdu(CFitsIO.USHORT_IMG, 200, 100);
    rimg.IsSuccessful.Should().BeTrue(rimg.Error?.ToString());

    var wcs = new WcsHeaderBuilder(fits)
      .SetTanWithCdMatrix(
        referenceWorldLongitudeDegrees: 180.0,
        referenceWorldLatitudeDegrees: 45.0,
        referencePixelX: 100.5,
        referencePixelY: 50.5,
        cd11: -0.00028, cd12: 0.0,
        cd21: 0.0, cd22: 0.00028)
      .SetCelestialSystem("ICRS", 2000.0, null, null);

    // Validate WCS subset
    fits.TryGetKeyString(FitsCommonKeywords.CTYPE1).Value.Should().Be("RA---TAN");
    fits.TryGetKeyString(FitsCommonKeywords.CTYPE2).Value.Should().Be("DEC--TAN");
    fits.TryGetKeyDouble(FitsCommonKeywords.CRVAL1).Value.Should().Be(180.0);
    fits.TryGetKeyDouble(FitsCommonKeywords.CRVAL2).Value.Should().Be(45.0);
    fits.TryGetKeyDouble(FitsCommonKeywords.CRPIX1).Value.Should().Be(100.5);
    fits.TryGetKeyDouble(FitsCommonKeywords.CRPIX2).Value.Should().Be(50.5);
    fits.TryGetKeyDouble(FitsCommonKeywords.CD1_1).Value.Should().BeApproximately(-0.00028, 1e-12);
    fits.TryGetKeyDouble(FitsCommonKeywords.CD2_2).Value.Should().BeApproximately(0.00028, 1e-12);
    fits.TryGetKeyString(FitsCommonKeywords.RADESYS).Value.Should().Be("ICRS");
    fits.TryGetKeyDouble(FitsCommonKeywords.EQUINOX).Value.Should().Be(2000.0);

    fits.Dispose();
    TestPaths.DeleteIfExists(path);
  }

  [TestMethod]
  public void WcsBuilder_SetSimpleTan_Writes_CDELT_Keywords()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");
    string path = TestPaths.GetTempFile($"wcs_simple_{Guid.NewGuid():N}.fits");
    TestPaths.DeleteIfExists(path);

    var created = FitsFile.Create("!" + path);
    created.IsSuccessful.Should().BeTrue(created.Error?.ToString());
    using var fits = created.Value;

    var rimg = fits.CreateImageHdu(CFitsIO.USHORT_IMG, 100, 100);
    rimg.IsSuccessful.Should().BeTrue(rimg.Error?.ToString());

    // Use SetSimpleTan which writes CDELT1/CDELT2 instead of CD matrix
    var wcs = new WcsHeaderBuilder(fits)
      .SetSimpleTan(
        referenceWorldLongitudeDegrees: 90.0,
        referenceWorldLatitudeDegrees: 30.0,
        referencePixelX: 50.0,
        referencePixelY: 50.0,
        degreesPerPixelX: -0.0005,
        degreesPerPixelY: 0.0005)
      .SetCelestialSystem("FK5", 2000.0);

    // Validate CDELT-based WCS
    fits.TryGetKeyString(FitsCommonKeywords.CTYPE1).Value.Should().Be("RA---TAN");
    fits.TryGetKeyString(FitsCommonKeywords.CTYPE2).Value.Should().Be("DEC--TAN");
    fits.TryGetKeyDouble(FitsCommonKeywords.CRVAL1).Value.Should().Be(90.0);
    fits.TryGetKeyDouble(FitsCommonKeywords.CRVAL2).Value.Should().Be(30.0);
    fits.TryGetKeyDouble(FitsCommonKeywords.CRPIX1).Value.Should().Be(50.0);
    fits.TryGetKeyDouble(FitsCommonKeywords.CRPIX2).Value.Should().Be(50.0);
    fits.TryGetKeyDouble(FitsCommonKeywords.CDELT1).Value.Should().BeApproximately(-0.0005, 1e-12);
    fits.TryGetKeyDouble(FitsCommonKeywords.CDELT2).Value.Should().BeApproximately(0.0005, 1e-12);
    fits.TryGetKeyString(FitsCommonKeywords.CUNIT1).Value.Should().Be("deg");
    fits.TryGetKeyString(FitsCommonKeywords.CUNIT2).Value.Should().Be("deg");
    fits.TryGetKeyString(FitsCommonKeywords.RADESYS).Value.Should().Be("FK5");
    fits.TryGetKeyDouble(FitsCommonKeywords.EQUINOX).Value.Should().Be(2000.0);

    fits.Dispose();
    TestPaths.DeleteIfExists(path);
  }
}

