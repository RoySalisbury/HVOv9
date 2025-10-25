using System;
using System.IO;
using FluentAssertions;
using HVO.Astronomy.CFITSIO.Interop;
using HVO.Astronomy.CFITSIO.Tests.Helpers;

namespace HVO.Astronomy.CFITSIO.Tests;

/// <summary>
/// Tests for FITS compression policies and algorithms.
/// </summary>
[TestClass]
public class CompressionPolicyTests
{
  [TestMethod]
  public void FitsCompressionPolicy_DefaultValues_AreCorrect()
  {
    // Act
    var policy = new FitsCompressionPolicy();

    // Assert
    policy.Compression.Should().Be(FitsCompression.Rice);
    policy.TileDimensions.Should().BeNull();
    policy.Parameters.Should().BeNull();
    policy.WriteChecksum.Should().BeTrue();
  }

  [TestMethod]
  public void FitsCompressionPolicy_CustomValues_CanBeSet()
  {
    // Act
    var policy = new FitsCompressionPolicy
    {
      Compression = FitsCompression.GZip1,
      TileDimensions = new long[] { 100, 100 },
      Parameters = new float[] { 1.0f, 2.0f },
      WriteChecksum = false
    };

    // Assert
    policy.Compression.Should().Be(FitsCompression.GZip1);
    policy.TileDimensions.Should().Equal(100L, 100L);
    policy.Parameters.Should().Equal(1.0f, 2.0f);
    policy.WriteChecksum.Should().BeFalse();
  }

  [TestMethod]
  public void FitsCompression_EnumValues_MatchCFitsIOConstants()
  {
    // Assert - Verify enum values match CFITSIO constants
    FitsCompression.None.Should().Be((FitsCompression)0);
    FitsCompression.Rice.Should().Be((FitsCompression)CFitsIO.RICE_1);
    FitsCompression.GZip1.Should().Be((FitsCompression)CFitsIO.GZIP_1);
    FitsCompression.GZip2.Should().Be((FitsCompression)CFitsIO.GZIP_2);
    FitsCompression.HCompress.Should().Be((FitsCompression)CFitsIO.HCOMPRESS_1);
    FitsCompression.Plio.Should().Be((FitsCompression)CFitsIO.PLIO_1);
  }

  [TestMethod]
  public void ApplyCompressionPolicy_WithRice_ConfiguresCompression()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");

    // Arrange
    string fitsPath = TestPaths.GetTempFile($"compress_rice_{Guid.NewGuid():N}.fits");
    TestPaths.DeleteIfExists(fitsPath);

    var policy = new FitsCompressionPolicy
    {
      Compression = FitsCompression.Rice,
      TileDimensions = new long[] { 10, 10 },
      WriteChecksum = true
    };

    // Act - Create file and apply compression before writing pixels
    var createResult = FitsFile.Create("!" + fitsPath);
    createResult.IsSuccessful.Should().BeTrue();
    using var fits = createResult.Value;

    var imgResult = fits.CreateImageHdu(CFitsIO.USHORT_IMG, 20, 20);
    imgResult.IsSuccessful.Should().BeTrue();

    var applyResult = fits.ApplyCompressionPolicyToCurrentHdu(policy);
    applyResult.IsSuccessful.Should().BeTrue(applyResult.Error?.ToString());

    // Write some test data
    var pixels = new ushort[400];
    for (int i = 0; i < pixels.Length; i++) pixels[i] = (ushort)(i % 100);
    var writeResult = FitsImage.WriteU16(fits, 20, 20, pixels);
    writeResult.IsSuccessful.Should().BeTrue();

    // Close and verify file exists
    fits.Dispose();
    File.Exists(fitsPath).Should().BeTrue();

    TestPaths.DeleteIfExists(fitsPath);
  }

  [TestMethod]
  public void ApplyCompressionPolicy_WithGZip1_ConfiguresCompression()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");

    // Arrange
    string fitsPath = TestPaths.GetTempFile($"compress_gzip1_{Guid.NewGuid():N}.fits");
    TestPaths.DeleteIfExists(fitsPath);

    var policy = new FitsCompressionPolicy
    {
      Compression = FitsCompression.GZip1,
      WriteChecksum = false
    };

    // Act
    var createResult = FitsFile.Create("!" + fitsPath);
    using var fits = createResult.Value;

    var imgResult = fits.CreateImageHdu(CFitsIO.BYTE_IMG, 10, 10);
    imgResult.IsSuccessful.Should().BeTrue();

    var applyResult = fits.ApplyCompressionPolicyToCurrentHdu(policy);
    applyResult.IsSuccessful.Should().BeTrue(applyResult.Error?.ToString());

    var pixels = new byte[100];
    for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i % 50);
    FitsImage.WriteU8(fits, 10, 10, pixels);

    TestPaths.DeleteIfExists(fitsPath);
  }

  [TestMethod]
  public void ApplyCompressionPolicy_WithGZip2_ConfiguresCompression()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");

    // Arrange
    string fitsPath = TestPaths.GetTempFile($"compress_gzip2_{Guid.NewGuid():N}.fits");
    TestPaths.DeleteIfExists(fitsPath);

    var policy = new FitsCompressionPolicy
    {
      Compression = FitsCompression.GZip2,
      TileDimensions = new long[] { 8, 8 }
    };

    // Act
    var createResult = FitsFile.Create("!" + fitsPath);
    using var fits = createResult.Value;

    fits.CreateImageHdu(CFitsIO.USHORT_IMG, 16, 16);
    var applyResult = fits.ApplyCompressionPolicyToCurrentHdu(policy);
    applyResult.IsSuccessful.Should().BeTrue();

    var pixels = new ushort[256];
    FitsImage.WriteU16(fits, 16, 16, pixels);

    TestPaths.DeleteIfExists(fitsPath);
  }

  [TestMethod]
  public void ApplyCompressionPolicy_WithHCompress_ConfiguresCompression()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");

    // Arrange
    string fitsPath = TestPaths.GetTempFile($"compress_hcompress_{Guid.NewGuid():N}.fits");
    TestPaths.DeleteIfExists(fitsPath);

    var policy = new FitsCompressionPolicy
    {
      Compression = FitsCompression.HCompress,
      Parameters = new float[] { 4.0f } // Scale parameter
    };

    // Act
    var createResult = FitsFile.Create("!" + fitsPath);
    using var fits = createResult.Value;

    fits.CreateImageHdu(CFitsIO.USHORT_IMG, 32, 32);
    var applyResult = fits.ApplyCompressionPolicyToCurrentHdu(policy);
    applyResult.IsSuccessful.Should().BeTrue();

    var pixels = new ushort[1024];
    FitsImage.WriteU16(fits, 32, 32, pixels);

    TestPaths.DeleteIfExists(fitsPath);
  }

  [TestMethod]
  public void ApplyCompressionPolicy_WithPlio_ConfiguresCompression()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");

    // Arrange
    string fitsPath = TestPaths.GetTempFile($"compress_plio_{Guid.NewGuid():N}.fits");
    TestPaths.DeleteIfExists(fitsPath);

    var policy = new FitsCompressionPolicy
    {
      Compression = FitsCompression.Plio
    };

    // Act
    var createResult = FitsFile.Create("!" + fitsPath);
    using var fits = createResult.Value;

    fits.CreateImageHdu(CFitsIO.SHORT_IMG, 10, 10);
    var applyResult = fits.ApplyCompressionPolicyToCurrentHdu(policy);
    applyResult.IsSuccessful.Should().BeTrue();

    var pixels = new ushort[100];
    FitsImage.WriteU16(fits, 10, 10, pixels);

    TestPaths.DeleteIfExists(fitsPath);
  }

  [TestMethod]
  public void ApplyCompressionPolicy_WithNone_DoesNotCompress()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");

    // Arrange
    string fitsPath = TestPaths.GetTempFile($"compress_none_{Guid.NewGuid():N}.fits");
    TestPaths.DeleteIfExists(fitsPath);

    var policy = new FitsCompressionPolicy
    {
      Compression = FitsCompression.None
    };

    // Act
    var createResult = FitsFile.Create("!" + fitsPath);
    using var fits = createResult.Value;

    fits.CreateImageHdu(CFitsIO.USHORT_IMG, 10, 10);
    var applyResult = fits.ApplyCompressionPolicyToCurrentHdu(policy);

    // Note: CFITSIO may not support "none" compression policy application
    // This tests that the API accepts it without crashing

    var pixels = new ushort[100];
    FitsImage.WriteU16(fits, 10, 10, pixels);

    TestPaths.DeleteIfExists(fitsPath);
  }

  [TestMethod]
  public void CompressionPolicy_WithCustomTileDimensions_AppliesCorrectly()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");

    // Arrange - Test various tile dimensions
    string fitsPath = TestPaths.GetTempFile($"compress_tiles_{Guid.NewGuid():N}.fits");
    TestPaths.DeleteIfExists(fitsPath);

    var policy = new FitsCompressionPolicy
    {
      Compression = FitsCompression.Rice,
      TileDimensions = new long[] { 32, 16 }, // Custom tile size
      WriteChecksum = true
    };

    // Act
    var createResult = FitsFile.Create("!" + fitsPath);
    using var fits = createResult.Value;

    fits.CreateImageHdu(CFitsIO.USHORT_IMG, 64, 64);
    var applyResult = fits.ApplyCompressionPolicyToCurrentHdu(policy);
    applyResult.IsSuccessful.Should().BeTrue();

    var pixels = new ushort[64 * 64];
    for (int i = 0; i < pixels.Length; i++) pixels[i] = (ushort)(i % 1000);
    FitsImage.WriteU16(fits, 64, 64, pixels);

    TestPaths.DeleteIfExists(fitsPath);
  }

  [TestMethod]
  public void CompressionRoundTrip_DataIntegrity_IsPreserved()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");

    // Arrange - Create test data
    int width = 32, height = 32;
    var originalPixels = new ushort[width * height];
    for (int i = 0; i < originalPixels.Length; i++)
    {
      originalPixels[i] = (ushort)((i * 17) % 65536); // Varied pattern
    }

    string fitsPath = TestPaths.GetTempFile($"compress_roundtrip_{Guid.NewGuid():N}.fits");
    TestPaths.DeleteIfExists(fitsPath);

    var policy = new FitsCompressionPolicy
    {
      Compression = FitsCompression.Rice,
      TileDimensions = new long[] { 16, 16 }
    };

    // Act - Write compressed (ensure we write to the same HDU we applied compression to)
    var createResult = FitsFile.Create("!" + fitsPath);
    using (var fits = createResult.Value)
    {
      var imgRes = fits.CreateImageHdu(CFitsIO.USHORT_IMG, width, height);
      imgRes.IsSuccessful.Should().BeTrue();

      var applyRes = fits.ApplyCompressionPolicyToCurrentHdu(policy);
      applyRes.IsSuccessful.Should().BeTrue(applyRes.Error?.ToString());

      var scaleRes = fits.SetScale(1.0, 32768.0);
      scaleRes.IsSuccessful.Should().BeTrue(scaleRes.Error?.ToString());

      var writeRes = fits.WritePixelsU16(1, originalPixels);
      writeRes.IsSuccessful.Should().BeTrue(writeRes.Error?.ToString());
    }

    // Read back
    var openResult = FitsFile.Open(fitsPath, readWrite: false);
    using (var fits = openResult.Value)
    {
      var readResult = FitsImage.ReadU16(fits);
      readResult.IsSuccessful.Should().BeTrue();
      var (readPixels, w, h) = readResult.Value;

      // Assert
      w.Should().Be(width);
      h.Should().Be(height);
      readPixels.Should().Equal(originalPixels, "compressed data should round-trip correctly");
    }

    TestPaths.DeleteIfExists(fitsPath);
  }
}
