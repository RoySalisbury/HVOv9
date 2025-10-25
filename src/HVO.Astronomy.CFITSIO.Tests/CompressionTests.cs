using System;
using System.IO;
using FluentAssertions;
using HVO.Astronomy.CFITSIO.Interop;
using HVO.Astronomy.CFITSIO.Tests.Helpers;

namespace HVO.Astronomy.CFITSIO.Tests;

[TestClass]
public class CompressionTests
{
  [TestMethod]
  public void CompressTo_Creates_New_File_And_Is_Openable()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");
    string srcPath = TestPaths.GetTempFile($"src_{Guid.NewGuid():N}.fits");
    string dstPath = TestPaths.GetTempFile($"dst_{Guid.NewGuid():N}.fits.fz");
    TestPaths.DeleteIfExists(srcPath);
    TestPaths.DeleteIfExists(dstPath);

    // Create small image
    var created = FitsFile.Create("!" + srcPath);
    created.IsSuccessful.Should().BeTrue(created.Error?.ToString());
    using (var fits = created.Value)
    {
      var rimg = fits.CreateImageHdu(CFitsIO.USHORT_IMG, 10, 10);
      rimg.IsSuccessful.Should().BeTrue(rimg.Error?.ToString());

      var buf = new ushort[100];
      for (int i = 0; i < buf.Length; i++) buf[i] = (ushort)i;
      var w = fits.WritePixelsU16(1, buf);
      w.IsSuccessful.Should().BeTrue(w.Error?.ToString());

      var comp = fits.CompressTo("!" + dstPath);
      comp.IsSuccessful.Should().BeTrue(comp.Error?.ToString());
    }

    File.Exists(dstPath).Should().BeTrue();

    // Open compressed file and check HDU count
    var opened = FitsFile.Open(dstPath, readWrite: false);
    opened.IsSuccessful.Should().BeTrue(opened.Error?.ToString());
    using var compressed = opened.Value;
    var hdus = compressed.GetNumberOfHdus();
    hdus.IsSuccessful.Should().BeTrue();
    Assert.IsGreaterThanOrEqualTo(1, hdus.Value);

    compressed.Dispose();
    TestPaths.DeleteIfExists(srcPath);
    TestPaths.DeleteIfExists(dstPath);
  }
}
