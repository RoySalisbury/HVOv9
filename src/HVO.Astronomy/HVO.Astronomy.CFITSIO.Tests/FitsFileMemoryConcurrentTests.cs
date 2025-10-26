using System;
using System.Threading.Tasks;
using FluentAssertions;
using HVO.Astronomy.CFITSIO.Interop;
using HVO.Astronomy.CFITSIO.Tests.Helpers;

namespace HVO.Astronomy.CFITSIO.Tests;

/// <summary>
/// Tests for concurrent/simultaneous use of multiple in-memory FITS files.
/// This validates that the shared static realloc callback works correctly
/// when multiple FitsFile instances are active at the same time.
/// </summary>
[TestClass]
public class FitsFileMemoryConcurrentTests
{
  [TestMethod]
  public void MultipleMemfiles_CreatedAndUsedConcurrently_WorkCorrectly()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available.");
    if (!string.Equals(Environment.GetEnvironmentVariable("HVO_ENABLE_MEMFILE_TESTS"), "1", StringComparison.Ordinal))
      Assert.Inconclusive("Memfile tests disabled. Set HVO_ENABLE_MEMFILE_TESTS=1 to enable.");

    // Create multiple in-memory FITS files simultaneously
    using var mem1 = FitsFile.CreateInMemory();
    using var mem2 = FitsFile.CreateInMemory();
    using var mem3 = FitsFile.CreateInMemory();

    // Write different data to each
    mem1.CreateImageHdu(CFitsIO.BYTE_IMG, 10, 10).IsSuccessful.Should().BeTrue();
    var pixels1 = new byte[100];
    for (int i = 0; i < pixels1.Length; i++) pixels1[i] = (byte)(i % 256);
    mem1.WritePixelsU8(1, pixels1).IsSuccessful.Should().BeTrue();

    mem2.CreateImageHdu(CFitsIO.USHORT_IMG, 20, 20).IsSuccessful.Should().BeTrue();
    var pixels2 = new ushort[400];
    for (int i = 0; i < pixels2.Length; i++) pixels2[i] = (ushort)(i * 13);
    mem2.WritePixelsU16(1, pixels2).IsSuccessful.Should().BeTrue();

    mem3.CreateImageHdu(CFitsIO.BYTE_IMG, 5, 5).IsSuccessful.Should().BeTrue();
    var pixels3 = new byte[25];
    for (int i = 0; i < pixels3.Length; i++) pixels3[i] = (byte)(i * 7);
    mem3.WritePixelsU8(1, pixels3).IsSuccessful.Should().BeTrue();

    // Convert to arrays while all are still open
    var bytes1 = mem1.ToArray();
    var bytes2 = mem2.ToArray();
    var bytes3 = mem3.ToArray();

    bytes1.Should().NotBeEmpty();
    bytes2.Should().NotBeEmpty();
    bytes3.Should().NotBeEmpty();

    // Verify each has correct data by re-opening
    using var check1 = FitsFile.OpenFromMemory(bytes1, readWrite: false);
    var read1 = FitsImage.ReadU8(check1);
    read1.IsSuccessful.Should().BeTrue();
    read1.Value.Pixels.Should().BeEquivalentTo(pixels1);

    using var check2 = FitsFile.OpenFromMemory(bytes2, readWrite: false);
    var read2 = FitsImage.ReadU16(check2);
    read2.IsSuccessful.Should().BeTrue();
    read2.Value.Pixels.Should().BeEquivalentTo(pixels2);

    using var check3 = FitsFile.OpenFromMemory(bytes3, readWrite: false);
    var read3 = FitsImage.ReadU8(check3);
    read3.IsSuccessful.Should().BeTrue();
    read3.Value.Pixels.Should().BeEquivalentTo(pixels3);
  }

  [TestMethod]
  public async Task MultipleMemfiles_InParallelTasks_WorkCorrectly()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available.");
    if (!string.Equals(Environment.GetEnvironmentVariable("HVO_ENABLE_MEMFILE_TESTS"), "1", StringComparison.Ordinal))
      Assert.Inconclusive("Memfile tests disabled. Set HVO_ENABLE_MEMFILE_TESTS=1 to enable.");

    // Run multiple memfile operations in parallel tasks
    var tasks = new Task<byte[]>[5];

    for (int i = 0; i < tasks.Length; i++)
    {
      int taskId = i;
      tasks[i] = Task.Run(() =>
      {
        using var mem = FitsFile.CreateInMemory();
        mem.CreateImageHdu(CFitsIO.BYTE_IMG, 8, 8).IsSuccessful.Should().BeTrue();

        var pixels = new byte[64];
        for (int j = 0; j < pixels.Length; j++)
          pixels[j] = (byte)((taskId * 10 + j) % 256);

        mem.WritePixelsU8(1, pixels).IsSuccessful.Should().BeTrue();
        return mem.ToArray();
      });
    }

    var results = await Task.WhenAll(tasks);

    // Verify all results are valid and distinct
    results.Should().HaveCount(5);
    foreach (var result in results)
    {
      result.Should().NotBeEmpty();
      (result.Length % 2880).Should().Be(0, "FITS files must be multiples of 2880 bytes");
    }

    // Spot check - all should have different content
    results[0].Should().NotEqual(results[1]);
    results[1].Should().NotEqual(results[2]);
  }
}
