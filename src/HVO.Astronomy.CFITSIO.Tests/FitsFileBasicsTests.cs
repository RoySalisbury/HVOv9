using System;
using System.IO;
using FluentAssertions;
using HVO.Astronomy.CFITSIO.Interop;
using HVO.Astronomy.CFITSIO.Tests.Helpers;

namespace HVO.Astronomy.CFITSIO.Tests;

[TestClass]
public class FitsFileBasicsTests
{
  [TestMethod]
  public void Create_Write_Read_U16_Image_Succeeds()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");
    // Arrange
    string path = TestPaths.GetTempFile($"u16_basic_{Guid.NewGuid():N}.fits");
    TestPaths.DeleteIfExists(path);
    int w = 8, h = 6;
    var pixels = new ushort[w * h];
    for (int y = 0; y < h; y++)
      for (int x = 0; x < w; x++)
        pixels[y * w + x] = (ushort)(x + y * 10);

    // Act
    var created = FitsFile.Create("!" + path);
    created.IsSuccessful.Should().BeTrue(created.Error?.ToString());
    using var fits = created.Value;

    var write = FitsImage.WriteU16(fits, w, h, pixels);
    write.IsSuccessful.Should().BeTrue(write.Error?.ToString());

    // Assert basics
    var ip = fits.GetImageParameters();
    ip.IsSuccessful.Should().BeTrue(ip.Error?.ToString());
    var (bitpix, naxis, naxes) = ip.Value;
    // CFITSIO normalizes USHORT_IMG (20) to BITPIX=16 with BZERO=32768
    bitpix.Should().Be(CFitsIO.SHORT_IMG);
    naxis.Should().Be(2);
    naxes[0].Should().Be(w);
    naxes[1].Should().Be(h);

    // Verify BSCALE/BZERO and some header keys
    var bscale = fits.TryGetKeyDouble(FitsCommonKeywords.BSCALE);
    var bzero = fits.TryGetKeyDouble(FitsCommonKeywords.BZERO);
    bscale.IsSuccessful.Should().BeTrue();
    bzero.IsSuccessful.Should().BeTrue();
    bscale.Value.Should().Be(1.0);
    bzero.Value.Should().Be(32768.0);

    var bunit = fits.TryGetKeyString(FitsCommonKeywords.BUNIT);
    bunit.IsSuccessful.Should().BeTrue();
    bunit.Value.Should().Be("ADU");

    // Roundtrip read
    var read = FitsImage.ReadU16(fits);
    read.IsSuccessful.Should().BeTrue(read.Error?.ToString());
    read.Value.Width.Should().Be(w);
    read.Value.Height.Should().Be(h);
    read.Value.Pixels.Should().Equal(pixels);

    // Cleanup
    fits.Dispose();
    File.Exists(path).Should().BeTrue();
    TestPaths.DeleteIfExists(path);
  }

  [TestMethod]
  public void Create_Write_Read_U8_Image_Succeeds()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");
    // Arrange
    string path = TestPaths.GetTempFile($"u8_basic_{Guid.NewGuid():N}.fits");
    TestPaths.DeleteIfExists(path);
    int w = 7, h = 5;
    var pixels = new byte[w * h];
    for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i % 251);

    // Act
    var created = FitsFile.Create("!" + path);
    created.IsSuccessful.Should().BeTrue(created.Error?.ToString());
    using var fits = created.Value;

    var write = FitsImage.WriteU8(fits, w, h, pixels);
    write.IsSuccessful.Should().BeTrue(write.Error?.ToString());

    // Assert basics
    var ip = fits.GetImageParameters();
    ip.IsSuccessful.Should().BeTrue(ip.Error?.ToString());
    var (bitpix, naxis, naxes) = ip.Value;
    bitpix.Should().Be(CFitsIO.BYTE_IMG);
    naxis.Should().Be(2);
    naxes[0].Should().Be(w);
    naxes[1].Should().Be(h);

    // Roundtrip read
    var read = FitsImage.ReadU8(fits);
    read.IsSuccessful.Should().BeTrue(read.Error?.ToString());
    read.Value.Width.Should().Be(w);
    read.Value.Height.Should().Be(h);
    read.Value.Pixels.Should().Equal(pixels);

    // Cleanup
    fits.Dispose();
    File.Exists(path).Should().BeTrue();
    TestPaths.DeleteIfExists(path);
  }
}
