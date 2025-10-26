#if HAS_SKIA
using System;
using System.IO;
using FluentAssertions;
using HVO.Astronomy.CFITSIO.Interop;
using HVO.Astronomy.CFITSIO.Tests.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;

namespace HVO.Astronomy.CFITSIO.Tests;

[TestClass]
[DoNotParallelize] // Uses memfile; avoid parallel crashes in test runner
public class FitsSkiaInMemoryTests
{
  private static bool MemfileEnabled => string.Equals(Environment.GetEnvironmentVariable("HVO_ENABLE_MEMFILE_TESTS"), "1", StringComparison.Ordinal);

  [TestMethod]
  public void Skia_ToFitsU16Bytes_RoundTripPixels_MatchGray8()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available.");
    if (!MemfileEnabled) Assert.Inconclusive("Memfile tests disabled. Set HVO_ENABLE_MEMFILE_TESTS=1 to enable.");

    // Create a 32x32 Gray8 gradient bitmap
    int w = 32, h = 32;
    using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Gray8, SKAlphaType.Opaque));
    var span = bmp.GetPixelSpan();
    for (int y = 0; y < h; y++)
      for (int x = 0; x < w; x++)
        span[y * w + x] = (byte)((x + y) % 256);

    // Convert to FITS bytes in memory (U16 grayscale)
    var rBytes = bmp.ToFitsU16BytesResult();
    Assert.IsTrue(rBytes.IsSuccessful, $"ToFitsU16BytesResult failed: {rBytes.Error}");
    var bytes = rBytes.Value;
    bytes.Should().NotBeNull();
    bytes.Length.Should().BeGreaterThan(0);
    (bytes.Length % 2880).Should().Be(0);

    // Open FITS from memory and read back U16, down-convert to Gray8, compare with original
    using var ff = FitsFile.OpenFromMemory(bytes, readWrite: false);
    var rRead = FitsImage.ReadU16(ff);
    if (rRead.IsFailure)
    {
      var firstErr = rRead.Error?.ToString();
      // Some writers place the image in an extension; try HDU #2
      var moved = ff.MoveToHdu(2);
      Assert.IsTrue(moved.IsSuccessful, $"MoveToHdu(2) failed. Initial ReadU16 error: {firstErr}");
      rRead = FitsImage.ReadU16(ff);
    }
    rRead.IsSuccessful.Should().BeTrue(rRead.Error?.ToString());
    var (u16, rw, rh) = rRead.Value;
    rw.Should().Be(w);
    rh.Should().Be(h);

    // Downconvert U16 → Gray8 and compare to original
    var gray = new byte[u16.Length];
    for (int i = 0; i < u16.Length; i++) gray[i] = (byte)(u16[i] >> 8);
    gray.Should().BeEquivalentTo(span.ToArray());
  }

  [TestMethod]
  public void Skia_RecompressFitsBytes_RoundTripReadable()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available.");
    if (!MemfileEnabled) Assert.Inconclusive("Memfile tests disabled. Set HVO_ENABLE_MEMFILE_TESTS=1 to enable.");

    // Make a simple 16x16 Gray8 pattern
    int w = 16, h = 16;
    using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Gray8, SKAlphaType.Opaque));
    var span = bmp.GetPixelSpan();
    for (int i = 0; i < span.Length; i++) span[i] = (byte)((i * 7) % 256);

    var rUncompressed = bmp.ToFitsU16BytesResult();
    rUncompressed.IsSuccessful.Should().BeTrue(rUncompressed.Error?.ToString());
    var uncompressed = rUncompressed.Value;

    // Recompress in-memory
    var rCompressed = uncompressed.RecompressFitsBytesResult();
    rCompressed.IsSuccessful.Should().BeTrue(rCompressed.Error?.ToString());
    var compressed = rCompressed.Value;

    compressed.Should().NotBeNull();
    compressed.Length.Should().BeGreaterThan(0);
    (compressed.Length % 2880).Should().Be(0);

    // Validate readable and pixel-equivalent (after U16→Gray8 downconvert)
    using var ff = FitsFile.OpenFromMemory(compressed, readWrite: false);
    ff.MoveToHdu(2).IsSuccessful.Should().BeTrue();
    var rRead = FitsImage.ReadU16(ff);
    rRead.IsSuccessful.Should().BeTrue(rRead.Error?.ToString());
    var (u16, rw, rh) = rRead.Value;
    rw.Should().Be(w);
    rh.Should().Be(h);

    var gray = new byte[u16.Length];
    for (int i = 0; i < u16.Length; i++) gray[i] = (byte)(u16[i] >> 8);
    gray.Should().BeEquivalentTo(span.ToArray());
  }
}
#endif
