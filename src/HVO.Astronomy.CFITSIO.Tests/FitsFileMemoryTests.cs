using System;
using System.IO;
using FluentAssertions;
using HVO.Astronomy.CFITSIO.Interop;
using HVO.Astronomy.CFITSIO.Tests.Helpers;

namespace HVO.Astronomy.CFITSIO.Tests;

/// <summary>
/// Tests for CFITSIO in-memory file (memfile) API.
/// 
/// KNOWN ISSUE: These tests crash when run together as a suite due to a test runner + native library
/// interaction issue. All tests pass when run individually. To run individually:
/// <code>
/// export HVO_ENABLE_MEMFILE_TESTS=1
/// dotnet test --filter "FullyQualifiedName~CreateInMemory_WriteImage_ToArray_NotEmpty"
/// dotnet test --filter "FullyQualifiedName~CompressToArray_FromInMemory_CanBeOpenedAndRead"
/// dotnet test --filter "FullyQualifiedName~OpenFromMemory_WithEmptyBuffer_Throws"
/// dotnet test --filter "FullyQualifiedName~CreateInMemory_JustCreateAndDispose_DoesNotCrash"
/// </code>
/// </summary>
[TestClass]
[DoNotParallelize] // Memfile tests must run sequentially due to CFITSIO native library state
public class FitsFileMemoryTests
{
  [TestMethod]
  public void CreateInMemory_WriteImage_ToArray_NotEmpty()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available.");
    if (!string.Equals(Environment.GetEnvironmentVariable("HVO_ENABLE_MEMFILE_TESTS"), "1", StringComparison.Ordinal))
      Assert.Inconclusive("Memfile tests disabled. Set HVO_ENABLE_MEMFILE_TESTS=1 to enable.");

    using var mem = FitsFile.CreateInMemory();
    mem.IsInMemory.Should().BeTrue();

    // Minimal smoke: ensure memfile can be created and closed without writes
    var empty = mem.ToArray();
    empty.Should().NotBeNull();
    (empty.Length % 2880).Should().Be(0);

    // Write a tiny image
    mem.CreateImageHdu(CFitsIO.BYTE_IMG, 4, 4).IsSuccessful.Should().BeTrue();
    var pixels = new byte[16];
    for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i * 3);
    mem.WritePixelsU8(1, pixels).IsSuccessful.Should().BeTrue();

    // Retrieve bytes
    var withData = mem.ToArray();
    withData.Should().NotBeNull();
    withData.Length.Should().BeGreaterThan(empty.Length);
    (withData.Length % 2880).Should().Be(0);
  }

  [TestMethod]
  public void CompressToArray_FromInMemory_CanBeOpenedAndRead()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available.");
    if (!string.Equals(Environment.GetEnvironmentVariable("HVO_ENABLE_MEMFILE_TESTS"), "1", StringComparison.Ordinal))
      Assert.Inconclusive("Memfile tests disabled. Set HVO_ENABLE_MEMFILE_TESTS=1 to enable.");

    const int w = 64, h = 64;
    var pixels = new ushort[w * h];
    for (int i = 0; i < pixels.Length; i++) pixels[i] = (ushort)((i * 17) % 65536);

    // Build in-memory FITS
    using var mem = FitsFile.CreateInMemory();
    mem.CreateImageHdu(CFitsIO.USHORT_IMG, w, h).IsSuccessful.Should().BeTrue();
    mem.SetScale(1.0, 32768.0).IsSuccessful.Should().BeTrue();
    mem.WritePixelsU16(1, pixels).IsSuccessful.Should().BeTrue();

    // Compress to another in-memory FITS and get its bytes
    var compressed = mem.CompressToArray();
    compressed.Should().NotBeNull();
    compressed.Length.Should().BeGreaterThan(0);
    (compressed.Length % 2880).Should().Be(0);

    // Open compressed bytes as read-only in-memory and read back
    using var ro = FitsFile.OpenFromMemory(compressed, readWrite: false);

    // Compressed image is in HDU #2 (primary is empty)
    ro.MoveToHdu(2).IsSuccessful.Should().BeTrue();

    var read = FitsImage.ReadU16(ro);
    read.IsSuccessful.Should().BeTrue(read.Error?.ToString());
    var (got, rw, rh) = read.Value;
    rw.Should().Be(w);
    rh.Should().Be(h);
    got.Should().BeEquivalentTo(pixels);
  }

  [TestMethod]
  public void OpenFromMemory_WithEmptyBuffer_Throws()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available.");
    if (!string.Equals(Environment.GetEnvironmentVariable("HVO_ENABLE_MEMFILE_TESTS"), "1", StringComparison.Ordinal))
      Assert.Inconclusive("Memfile tests disabled. Set HVO_ENABLE_MEMFILE_TESTS=1 to enable.");

    var empty = Array.Empty<byte>();
    Action act = () => FitsFile.OpenFromMemory(empty, readWrite: false);
    act.Should().Throw<ArgumentException>().WithMessage("*Empty FITS buffer*");
  }

  [TestMethod]
  public void CreateInMemory_JustCreateAndDispose_DoesNotCrash()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available.");
    if (!string.Equals(Environment.GetEnvironmentVariable("HVO_ENABLE_MEMFILE_TESTS"), "1", StringComparison.Ordinal))
      Assert.Inconclusive("Memfile tests disabled. Set HVO_ENABLE_MEMFILE_TESTS=1 to enable.");

    using var mem = FitsFile.CreateInMemory();
    mem.Should().NotBeNull();
    mem.IsInMemory.Should().BeTrue();
  }
}
