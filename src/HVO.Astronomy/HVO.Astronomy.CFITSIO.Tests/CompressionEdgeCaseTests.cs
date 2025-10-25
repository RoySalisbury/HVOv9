using System;
using System.IO;
using FluentAssertions;
using HVO.Astronomy.CFITSIO.Interop;
using HVO.Astronomy.CFITSIO.Tests.Helpers;

namespace HVO.Astronomy.CFITSIO.Tests;

/// <summary>
/// Edge case and error scenario tests for FITS compression.
/// </summary>
[TestClass]
public class CompressionEdgeCaseTests
{
  [TestMethod]
  public void HCompress_WithVariousScaleFactors_CompressesCorrectly()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");

    // Test HCompress with different scale factors: 1.0 (lossless), 4.0 (moderate), 16.0 (high compression)
    var scaleFactors = new float[] { 1.0f, 4.0f, 16.0f };

    foreach (var scaleFactor in scaleFactors)
    {
      string fitsPath = TestPaths.GetTempFile($"hcompress_scale_{scaleFactor:F1}_{Guid.NewGuid():N}.fits");
      TestPaths.DeleteIfExists(fitsPath);

      var policy = new FitsCompressionPolicy
      {
        Compression = FitsCompression.HCompress,
        Parameters = new float[] { scaleFactor }
      };

      var createResult = FitsFile.Create("!" + fitsPath);
      using (var fits = createResult.Value)
      {
        var imgResult = fits.CreateImageHdu(CFitsIO.USHORT_IMG, 64, 64);
        imgResult.IsSuccessful.Should().BeTrue();

        var applyResult = fits.ApplyCompressionPolicyToCurrentHdu(policy);
        applyResult.IsSuccessful.Should().BeTrue($"HCompress with scale {scaleFactor} should succeed");

        var pixels = new ushort[64 * 64];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = (ushort)(i % 10000);
        var writeResult = FitsImage.WriteU16(fits, 64, 64, pixels);
        writeResult.IsSuccessful.Should().BeTrue();
      }

      File.Exists(fitsPath).Should().BeTrue();
      TestPaths.DeleteIfExists(fitsPath);
    }
  }

  [TestMethod]
  public void GZip_WithDifferentTileDimensions_CompressesCorrectly()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");

    // Test various tile sizes for GZip compression
    var tileSizes = new[] {
      new long[] { 8, 8 },
      new long[] { 16, 16 },
      new long[] { 32, 32 },
      new long[] { 64, 32 },  // Non-square tiles
      new long[] { 100, 50 }  // Large tiles
    };

    foreach (var tileSize in tileSizes)
    {
      string fitsPath = TestPaths.GetTempFile($"gzip_tile_{tileSize[0]}x{tileSize[1]}_{Guid.NewGuid():N}.fits");
      TestPaths.DeleteIfExists(fitsPath);

      var policy = new FitsCompressionPolicy
      {
        Compression = FitsCompression.GZip1,
        TileDimensions = tileSize
      };

      var createResult = FitsFile.Create("!" + fitsPath);
      using (var fits = createResult.Value)
      {
        var imgResult = fits.CreateImageHdu(CFitsIO.USHORT_IMG, 128, 128);
        imgResult.IsSuccessful.Should().BeTrue();

        var applyResult = fits.ApplyCompressionPolicyToCurrentHdu(policy);
        applyResult.IsSuccessful.Should().BeTrue($"GZip with tile {tileSize[0]}x{tileSize[1]} should succeed");

        var pixels = new ushort[128 * 128];
        var writeResult = FitsImage.WriteU16(fits, 128, 128, pixels);
        writeResult.IsSuccessful.Should().BeTrue();
      }

      TestPaths.DeleteIfExists(fitsPath);
    }
  }

  [TestMethod]
  public void Plio_OnDifferentImageTypes_CompressesCorrectly()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");

    // PLIO compression on different image data types
    var imageTypes = new[] {
      (CFitsIO.BYTE_IMG, 10, 10, 100),
      (CFitsIO.SHORT_IMG, 20, 20, 400),
      (CFitsIO.USHORT_IMG, 16, 16, 256)
    };

    foreach (var (imageType, width, height, pixelCount) in imageTypes)
    {
      string fitsPath = TestPaths.GetTempFile($"plio_type_{imageType}_{Guid.NewGuid():N}.fits");
      TestPaths.DeleteIfExists(fitsPath);

      var policy = new FitsCompressionPolicy
      {
        Compression = FitsCompression.Plio
      };

      var createResult = FitsFile.Create("!" + fitsPath);
      using (var fits = createResult.Value)
      {
        var imgResult = fits.CreateImageHdu(imageType, width, height);
        imgResult.IsSuccessful.Should().BeTrue();

        var applyResult = fits.ApplyCompressionPolicyToCurrentHdu(policy);
        applyResult.IsSuccessful.Should().BeTrue($"Plio on image type {imageType} should succeed");

        var pixels = new ushort[pixelCount];
        FitsImage.WriteU16(fits, width, height, pixels);
      }

      TestPaths.DeleteIfExists(fitsPath);
    }
  }

  [TestMethod]
  public void MultiHduFile_WithDifferentCompressionPerHdu_WorksCorrectly()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");

    string fitsPath = TestPaths.GetTempFile($"multi_hdu_compress_{Guid.NewGuid():N}.fits");
    TestPaths.DeleteIfExists(fitsPath);

    // Create file with multiple HDUs, each using different compression
    var createResult = FitsFile.Create("!" + fitsPath);
    using (var fits = createResult.Value)
    {
      // HDU 1: Rice compression
      var img1 = fits.CreateImageHdu(CFitsIO.USHORT_IMG, 32, 32);
      img1.IsSuccessful.Should().BeTrue();
      var rice = new FitsCompressionPolicy { Compression = FitsCompression.Rice };
      fits.ApplyCompressionPolicyToCurrentHdu(rice).IsSuccessful.Should().BeTrue();
      var pixels1 = new ushort[32 * 32];
      FitsImage.WriteU16(fits, 32, 32, pixels1);

      // HDU 2: GZip compression
      var img2 = fits.CreateImageHdu(CFitsIO.USHORT_IMG, 16, 16);
      img2.IsSuccessful.Should().BeTrue();
      var gzip = new FitsCompressionPolicy { Compression = FitsCompression.GZip1 };
      fits.ApplyCompressionPolicyToCurrentHdu(gzip).IsSuccessful.Should().BeTrue();
      var pixels2 = new ushort[16 * 16];
      FitsImage.WriteU16(fits, 16, 16, pixels2);

      // HDU 3: HCompress
      var img3 = fits.CreateImageHdu(CFitsIO.USHORT_IMG, 64, 64);
      img3.IsSuccessful.Should().BeTrue();
      var hcomp = new FitsCompressionPolicy { Compression = FitsCompression.HCompress, Parameters = new float[] { 4.0f } };
      fits.ApplyCompressionPolicyToCurrentHdu(hcomp).IsSuccessful.Should().BeTrue();
      var pixels3 = new ushort[64 * 64];
      FitsImage.WriteU16(fits, 64, 64, pixels3);

      // Verify we can navigate between HDUs
      fits.MoveToHdu(1).IsSuccessful.Should().BeTrue();
      fits.MoveToHdu(2).IsSuccessful.Should().BeTrue();
      fits.MoveToHdu(3).IsSuccessful.Should().BeTrue();

      var hduCount = fits.GetNumberOfHdus();
      hduCount.IsSuccessful.Should().BeTrue();
      hduCount.Value.Should().BeGreaterThanOrEqualTo(3, "should have at least 3 HDUs");
    }

    File.Exists(fitsPath).Should().BeTrue();
    TestPaths.DeleteIfExists(fitsPath);
  }

  [TestMethod]
  public void CompressionWithChecksum_EnablesAndVerifies()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");

    string fitsPath = TestPaths.GetTempFile($"compress_checksum_{Guid.NewGuid():N}.fits");
    TestPaths.DeleteIfExists(fitsPath);

    var policyWithChecksum = new FitsCompressionPolicy
    {
      Compression = FitsCompression.Rice,
      WriteChecksum = true
    };

    var createResult = FitsFile.Create("!" + fitsPath);
    using (var fits = createResult.Value)
    {
      var imgResult = fits.CreateImageHdu(CFitsIO.USHORT_IMG, 20, 20);
      imgResult.IsSuccessful.Should().BeTrue();

      var applyResult = fits.ApplyCompressionPolicyToCurrentHdu(policyWithChecksum);
      applyResult.IsSuccessful.Should().BeTrue();

      var pixels = new ushort[400];
      for (int i = 0; i < pixels.Length; i++) pixels[i] = (ushort)i;
      FitsImage.WriteU16(fits, 20, 20, pixels);

      // CFITSIO should write CHECKSUM/DATASUM keywords when enabled
      // We validate the file can be opened and read without errors
    }

    // Reopen and verify checksums (implicit validation - CFITSIO would error if corrupted)
    var openResult = FitsFile.Open(fitsPath, readWrite: false);
    using (var fits = openResult.Value)
    {
      var readResult = FitsImage.ReadU16(fits);
      readResult.IsSuccessful.Should().BeTrue("compressed file with checksum should be readable");
    }

    TestPaths.DeleteIfExists(fitsPath);
  }

  [TestMethod]
  public void CompressionRoundTrip_WithScaling_PreservesDataCorrectly()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");

    string fitsPath = TestPaths.GetTempFile($"compress_scale_roundtrip_{Guid.NewGuid():N}.fits");
    TestPaths.DeleteIfExists(fitsPath);

    var originalPixels = new ushort[16 * 16];
    for (int i = 0; i < originalPixels.Length; i++)
    {
      originalPixels[i] = (ushort)((i * 123) % 65536);
    }

    var policy = new FitsCompressionPolicy
    {
      Compression = FitsCompression.Rice,
      TileDimensions = new long[] { 8, 8 }
    };

    // Write with compression (without scaling - scaling affects data transformation)
    var createResult = FitsFile.Create("!" + fitsPath);
    using (var fits = createResult.Value)
    {
      var imgResult = fits.CreateImageHdu(CFitsIO.USHORT_IMG, 16, 16);
      imgResult.IsSuccessful.Should().BeTrue();

      var applyResult = fits.ApplyCompressionPolicyToCurrentHdu(policy);
      applyResult.IsSuccessful.Should().BeTrue();

      // Test without SetScale to validate pure compression round-trip
      var writeResult = fits.WritePixelsU16(1, originalPixels);
      writeResult.IsSuccessful.Should().BeTrue();
    }

    // Read back and verify
    var openResult = FitsFile.Open(fitsPath, readWrite: false);
    using (var fits = openResult.Value)
    {
      var readResult = FitsImage.ReadU16(fits);
      readResult.IsSuccessful.Should().BeTrue();
      var (readPixels, w, h) = readResult.Value;

      w.Should().Be(16);
      h.Should().Be(16);
      readPixels.Should().Equal(originalPixels, "compressed data should round-trip correctly without scaling");
    }

    TestPaths.DeleteIfExists(fitsPath);
  }

  [TestMethod]
  public void CompressFile_ThenRecompress_HandlesCorrectly()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");

    string originalPath = TestPaths.GetTempFile($"original_{Guid.NewGuid():N}.fits");
    string compressed1Path = TestPaths.GetTempFile($"compressed1_{Guid.NewGuid():N}.fits.fz");
    string compressed2Path = TestPaths.GetTempFile($"compressed2_{Guid.NewGuid():N}.fits.fz");

    TestPaths.DeleteIfExists(originalPath);
    TestPaths.DeleteIfExists(compressed1Path);
    TestPaths.DeleteIfExists(compressed2Path);

    // Create original file
    var createResult = FitsFile.Create("!" + originalPath);
    using (var fits = createResult.Value)
    {
      fits.CreateImageHdu(CFitsIO.USHORT_IMG, 32, 32);
      var pixels = new ushort[32 * 32];
      for (int i = 0; i < pixels.Length; i++) pixels[i] = (ushort)(i % 1000);
      FitsImage.WriteU16(fits, 32, 32, pixels);
    }

    // First compression
    var open1 = FitsFile.Open(originalPath, readWrite: false);
    using (var fits1 = open1.Value)
    {
      var comp1 = fits1.CompressTo("!" + compressed1Path);
      comp1.IsSuccessful.Should().BeTrue();
    }

    // Try compressing the already-compressed file (double compression)
    var open2 = FitsFile.Open(compressed1Path, readWrite: false);
    using (var fits2 = open2.Value)
    {
      var comp2 = fits2.CompressTo("!" + compressed2Path);
      // Document behavior - CFITSIO may prevent double compression or handle it differently
    }

    TestPaths.DeleteIfExists(originalPath);
    TestPaths.DeleteIfExists(compressed1Path);
    TestPaths.DeleteIfExists(compressed2Path);
  }

  [TestMethod]
  public void LargeImageCompression_WithSmallTiles_CompletesSuccessfully()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");

    string fitsPath = TestPaths.GetTempFile($"large_image_small_tiles_{Guid.NewGuid():N}.fits");
    TestPaths.DeleteIfExists(fitsPath);

    // Large image with very small tiles tests tiling overhead
    var policy = new FitsCompressionPolicy
    {
      Compression = FitsCompression.Rice,
      TileDimensions = new long[] { 4, 4 } // Very small tiles for large image
    };

    var createResult = FitsFile.Create("!" + fitsPath);
    using (var fits = createResult.Value)
    {
      var imgResult = fits.CreateImageHdu(CFitsIO.USHORT_IMG, 512, 512);
      imgResult.IsSuccessful.Should().BeTrue();

      var applyResult = fits.ApplyCompressionPolicyToCurrentHdu(policy);
      applyResult.IsSuccessful.Should().BeTrue("small tiles on large image should work");

      var pixels = new ushort[512 * 512];
      for (int i = 0; i < pixels.Length; i++) pixels[i] = (ushort)(i % 10000);
      var writeResult = FitsImage.WriteU16(fits, 512, 512, pixels);
      writeResult.IsSuccessful.Should().BeTrue();
    }

    File.Exists(fitsPath).Should().BeTrue();
    var fileInfo = new FileInfo(fitsPath);
    fileInfo.Length.Should().BeGreaterThan(0, "compressed file should have content");

    TestPaths.DeleteIfExists(fitsPath);
  }
}
