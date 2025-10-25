using System;
using System.IO;
using FluentAssertions;
using HVO.Astronomy.CFITSIO.Tests.Helpers;
using SkiaSharp;

namespace HVO.Astronomy.CFITSIO.Tests;

/// <summary>
/// Tests for SkiaSharp integration and FITS image conversion.
/// </summary>
[TestClass]
public class SkiaFitsTests
{
  [TestMethod]
  public void SaveAsFitsU16_FromBitmap_CreatesValidFitsFile()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");

    // Arrange - Create a test bitmap (8x6 Gray8)
    int width = 8, height = 6;
    var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque));
    var pixels = bitmap.GetPixelSpan();

    // Fill with test pattern (gradient)
    for (int y = 0; y < height; y++)
    {
      for (int x = 0; x < width; x++)
      {
        pixels[y * width + x] = (byte)((x + y) * 10);
      }
    }

    string fitsPath = TestPaths.GetTempFile($"skia_bitmap_{Guid.NewGuid():N}.fits");
    TestPaths.DeleteIfExists(fitsPath);

    // Act
    var result = bitmap.SaveAsFitsU16(fitsPath, overwrite: true);

    // Assert
    result.IsSuccessful.Should().BeTrue(result.Error?.ToString());
    File.Exists(fitsPath).Should().BeTrue();

    // Verify we can read it back
    var openResult = FitsFile.Open(fitsPath, readWrite: false);
    openResult.IsSuccessful.Should().BeTrue();
    using var fits = openResult.Value;

    var imgParams = fits.GetImageParameters();
    imgParams.IsSuccessful.Should().BeTrue();
    var (bitpix, naxis, naxes) = imgParams.Value;
    naxis.Should().Be(2);
    naxes[0].Should().Be(width);
    naxes[1].Should().Be(height);

    TestPaths.DeleteIfExists(fitsPath);
    bitmap.Dispose();
  }

  [TestMethod]
  public void SaveAsFitsU16_FromImage_CreatesValidFitsFile()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");

    // Arrange - Create a bitmap and convert to SKImage
    int width = 4, height = 4;
    var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque));
    var pixels = bitmap.GetPixelSpan();
    for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i * 16);

    using var image = SKImage.FromBitmap(bitmap);
    string fitsPath = TestPaths.GetTempFile($"skia_image_{Guid.NewGuid():N}.fits");
    TestPaths.DeleteIfExists(fitsPath);

    // Act
    var result = image.SaveAsFitsU16(fitsPath, overwrite: true);

    // Assert
    result.IsSuccessful.Should().BeTrue(result.Error?.ToString());
    File.Exists(fitsPath).Should().BeTrue();

    TestPaths.DeleteIfExists(fitsPath);
    bitmap.Dispose();
  }

  [TestMethod]
  public void SaveAsFitsU16_WithHeaderStamping_AppliesCustomHeaders()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");

    // Arrange
    var bitmap = new SKBitmap(new SKImageInfo(10, 10, SKColorType.Gray8, SKAlphaType.Opaque));
    string fitsPath = TestPaths.GetTempFile($"skia_stamped_{Guid.NewGuid():N}.fits");
    TestPaths.DeleteIfExists(fitsPath);

    // Act - Save with custom header
    var result = bitmap.SaveAsFitsU16(fitsPath, overwrite: true, stampHeader: fits =>
    {
      fits.WriteKeyString("OBSERVER", "test-observer", "Observer name");
      fits.WriteKeyInt32("EXPOSURE", 300, "Exposure time (s)");
    });

    // Assert
    result.IsSuccessful.Should().BeTrue(result.Error?.ToString());

    // Verify headers
    var openResult = FitsFile.Open(fitsPath, readWrite: false);
    using var fits = openResult.Value;
    fits.TryGetKeyString("OBSERVER").Value.Should().Be("test-observer");
    fits.TryGetKeyInt32("EXPOSURE").Value.Should().Be(300);

    TestPaths.DeleteIfExists(fitsPath);
    bitmap.Dispose();
  }

  [TestMethod]
  public void LoadFitsToBitmap_WithU16Preference_LoadsCorrectly()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");

    // Arrange - Create a FITS file with known data
    string fitsPath = TestPaths.GetTempFile($"skia_load_u16_{Guid.NewGuid():N}.fits");
    TestPaths.DeleteIfExists(fitsPath);

    int width = 8, height = 6;
    var pixels = new ushort[width * height];
    for (int i = 0; i < pixels.Length; i++) pixels[i] = (ushort)(i * 1000); // High values

    var createResult = FitsFile.Create("!" + fitsPath);
    using (var fits = createResult.Value)
    {
      FitsImage.WriteU16(fits, width, height, pixels);
    }

    // Act
    var loadResult = SkiaFitsExtensions.LoadFitsToBitmap(fitsPath, preferU16: true);

    // Assert
    loadResult.IsSuccessful.Should().BeTrue(loadResult.Error?.ToString());
    using var bitmap = loadResult.Value;
    bitmap.Width.Should().Be(width);
    bitmap.Height.Should().Be(height);
    bitmap.ColorType.Should().Be(SKColorType.Gray8);

    // Verify downconversion (>> 8)
    var loadedPixels = bitmap.GetPixelSpan();
    for (int i = 0; i < loadedPixels.Length; i++)
    {
      byte expected = (byte)(pixels[i] >> 8);
      loadedPixels[i].Should().Be(expected);
    }

    TestPaths.DeleteIfExists(fitsPath);
  }

  [TestMethod]
  public void LoadFitsToBitmap_WithU8Direct_LoadsCorrectly()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");

    // Arrange - Create a FITS file with U8 data
    string fitsPath = TestPaths.GetTempFile($"skia_load_u8_{Guid.NewGuid():N}.fits");
    TestPaths.DeleteIfExists(fitsPath);

    int width = 4, height = 4;
    var pixels = new byte[width * height];
    for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i * 15);

    var createResult = FitsFile.Create("!" + fitsPath);
    using (var fits = createResult.Value)
    {
      FitsImage.WriteU8(fits, width, height, pixels);
    }

    // Act
    var loadResult = SkiaFitsExtensions.LoadFitsToBitmap(fitsPath, preferU16: false);

    // Assert
    loadResult.IsSuccessful.Should().BeTrue(loadResult.Error?.ToString());
    using var bitmap = loadResult.Value;
    bitmap.Width.Should().Be(width);
    bitmap.Height.Should().Be(height);

    // Verify exact match
    var loadedPixels = bitmap.GetPixelSpan();
    for (int i = 0; i < pixels.Length; i++)
    {
      loadedPixels[i].Should().Be(pixels[i]);
    }

    TestPaths.DeleteIfExists(fitsPath);
  }

  [TestMethod]
  public void SavePng_CreatesValidPngFile()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");

    // Arrange
    var bitmap = new SKBitmap(new SKImageInfo(10, 10, SKColorType.Gray8, SKAlphaType.Opaque));
    var pixels = bitmap.GetPixelSpan();
    for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i * 2);

    string pngPath = TestPaths.GetTempFile($"skia_test_{Guid.NewGuid():N}.png");
    TestPaths.DeleteIfExists(pngPath);

    // Act
    bitmap.SavePng(pngPath, pngQuality: 95);

    // Assert
    File.Exists(pngPath).Should().BeTrue();
    new FileInfo(pngPath).Length.Should().BeGreaterThan(0);

    TestPaths.DeleteIfExists(pngPath);
    bitmap.Dispose();
  }

  [TestMethod]
  public void SaveJpeg_CreatesValidJpegFile()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");

    // Arrange
    var bitmap = new SKBitmap(new SKImageInfo(10, 10, SKColorType.Gray8, SKAlphaType.Opaque));
    var pixels = bitmap.GetPixelSpan();
    for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i * 2);

    string jpegPath = TestPaths.GetTempFile($"skia_test_{Guid.NewGuid():N}.jpg");
    TestPaths.DeleteIfExists(jpegPath);

    // Act
    bitmap.SaveJpeg(jpegPath, jpegQuality: 85);

    // Assert
    File.Exists(jpegPath).Should().BeTrue();
    new FileInfo(jpegPath).Length.Should().BeGreaterThan(0);

    TestPaths.DeleteIfExists(jpegPath);
    bitmap.Dispose();
  }

  [TestMethod]
  public void SaveJpeg_WithInvalidQuality_ThrowsException()
  {
    // Arrange
    var bitmap = new SKBitmap(new SKImageInfo(10, 10, SKColorType.Gray8, SKAlphaType.Opaque));
    string jpegPath = TestPaths.GetTempFile($"skia_invalid_{Guid.NewGuid():N}.jpg");

    // Act & Assert
    Action act = () => bitmap.SaveJpeg(jpegPath, jpegQuality: 101);
    act.Should().Throw<ArgumentOutOfRangeException>();

    bitmap.Dispose();
  }

  [TestMethod]
  public void RoundTrip_SaveAndLoad_PreservesData()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");

    // Arrange - Create original bitmap
    int width = 16, height = 12;
    var originalBitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque));
    var originalPixels = originalBitmap.GetPixelSpan();
    for (int i = 0; i < originalPixels.Length; i++)
    {
      originalPixels[i] = (byte)(i % 256);
    }

    string fitsPath = TestPaths.GetTempFile($"skia_roundtrip_{Guid.NewGuid():N}.fits");
    TestPaths.DeleteIfExists(fitsPath);

    // Act - Save and load
    var saveResult = originalBitmap.SaveAsFitsU16(fitsPath);
    saveResult.IsSuccessful.Should().BeTrue();

    var loadResult = SkiaFitsExtensions.LoadFitsToBitmap(fitsPath, preferU16: true);
    loadResult.IsSuccessful.Should().BeTrue();

    // Assert - Verify dimensions
    using var loadedBitmap = loadResult.Value;
    loadedBitmap.Width.Should().Be(width);
    loadedBitmap.Height.Should().Be(height);

    // Note: Due to U16 round-trip (byte expanded to ushort, then downconverted),
    // we expect the data to match when considering the >> 8 conversion
    var loadedPixels = loadedBitmap.GetPixelSpan();
    for (int i = 0; i < originalPixels.Length; i++)
    {
      // Original byte expanded to (byte << 8 | byte), then read back as >> 8 gives original byte
      loadedPixels[i].Should().Be(originalPixels[i]);
    }

    TestPaths.DeleteIfExists(fitsPath);
    originalBitmap.Dispose();
  }
}
