using System;
using System.IO;
using FluentAssertions;
using HVO.Astronomy.CFITSIO.Interop;
using HVO.Astronomy.CFITSIO.Tests.Helpers;

namespace HVO.Astronomy.CFITSIO.Tests;

[TestClass]
public class FitsFileAdvancedTests
{
  [TestMethod]
  public void MultiHdu_Navigation_And_Info_Work()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");

    string path = TestPaths.GetTempFile($"nav_{Guid.NewGuid():N}.fits");
    TestPaths.DeleteIfExists(path);

    // Create a file with two image HDUs
    var c = FitsFile.Create("!" + path);
    c.IsSuccessful.Should().BeTrue(c.Error?.ToString());
    using var fits = c.Value;

    var w1 = 4; var h1 = 3; var p1 = new byte[w1 * h1];
    for (int i = 0; i < p1.Length; i++) p1[i] = (byte)i;
    FitsImage.WriteU8(fits, w1, h1, p1).IsSuccessful.Should().BeTrue();

    var w2 = 2; var h2 = 2; var p2 = new ushort[w2 * h2];
    for (int i = 0; i < p2.Length; i++) p2[i] = (ushort)(100 + i);
    FitsImage.WriteU16(fits, w2, h2, p2).IsSuccessful.Should().BeTrue();

    // There should be two HDUs
    var hduCount = fits.GetNumberOfHdus();
    hduCount.IsSuccessful.Should().BeTrue();
    hduCount.Value.Should().BeGreaterThanOrEqualTo(2);

    // We're currently on the second HDU (latest created)
    var info2 = fits.GetCurrentHduInfo();
    info2.IsSuccessful.Should().BeTrue();
    var (type2, abs2) = info2.Value;
    type2.Should().Be(CFitsIO.IMAGE_HDU);
    abs2.Should().Be(2);

    // Move to the first HDU and verify
    var movedType1 = fits.MoveToHdu(1);
    movedType1.IsSuccessful.Should().BeTrue();
    movedType1.Value.Should().Be(CFitsIO.IMAGE_HDU);

    var info1 = fits.GetCurrentHduInfo();
    info1.IsSuccessful.Should().BeTrue();
    var (_, abs1) = info1.Value;
    abs1.Should().Be(1);

    // Move forward by one (relative) and verify
    var movedType2 = fits.MoveBy(1);
    movedType2.IsSuccessful.Should().BeTrue();
    movedType2.Value.Should().Be(CFitsIO.IMAGE_HDU);

    var infoAgain = fits.GetCurrentHduInfo();
    infoAgain.IsSuccessful.Should().BeTrue();
    infoAgain.Value.AbsoluteHduNumber.Should().Be(2);

    TestPaths.DeleteIfExists(path);
  }

  [TestMethod]
  public void DeleteHeaderKey_RemovesKey()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");

    string path = TestPaths.GetTempFile($"delkey_{Guid.NewGuid():N}.fits");
    TestPaths.DeleteIfExists(path);

    var c = FitsFile.Create("!" + path);
    using var fits = c.Value;

    // Create a simple image which also writes some header keys (BUNIT, BITDEPTH)
    FitsImage.WriteU8(fits, 4, 4, new byte[16]).IsSuccessful.Should().BeTrue();

    // Delete BITDEPTH and verify it is gone
    var del = fits.DeleteHeaderKey("BITDEPTH");
    del.IsSuccessful.Should().BeTrue(del.Error?.ToString());

    var tryGet = fits.TryGetKeyString("BITDEPTH");
    tryGet.IsSuccessful.Should().BeTrue();
    tryGet.Value.Should().BeNull();

    TestPaths.DeleteIfExists(path);
  }

  [TestMethod]
  public void CompressTo_NewFile_CanBeReadBack()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");

    string src = TestPaths.GetTempFile($"src_{Guid.NewGuid():N}.fits");
    string dst = TestPaths.GetTempFile($"dst_{Guid.NewGuid():N}.fits");
    TestPaths.DeleteIfExists(src);
    TestPaths.DeleteIfExists(dst);

    // Prepare a small U16 image
    var c = FitsFile.Create("!" + src);
    using (var fits = c.Value)
    {
      int w = 10, h = 8;
      var pixels = new ushort[w * h];
      for (int i = 0; i < pixels.Length; i++) pixels[i] = (ushort)(i * 3 % 65536);
      FitsImage.WriteU16(fits, w, h, pixels).IsSuccessful.Should().BeTrue();
    }

    // Compress the file to a new destination
    var open = FitsFile.Open(src, readWrite: false);
    using (var fits = open.Value)
    {
      var r = fits.CompressTo("!" + dst);
      r.IsSuccessful.Should().BeTrue(r.Error?.ToString());
    }

    // Read back from the compressed file
    var openDst = FitsFile.Open(dst, readWrite: false);
    using (var ff = openDst.Value)
    {
      // Find the first 2D image HDU and read it back
      var countRes = ff.GetNumberOfHdus();
      countRes.IsSuccessful.Should().BeTrue();
      int count = countRes.Value;

      bool readOk = false;
      for (int i = 1; i <= count; i++)
      {
        ff.MoveToHdu(i).IsSuccessful.Should().BeTrue();
        var ip = ff.GetImageParameters();
        if (ip.IsSuccessful && ip.Value.NumberOfAxes >= 2)
        {
          var read = FitsImage.ReadU16(ff);
          if (read.IsSuccessful)
          {
            read.Value.Width.Should().Be(10);
            read.Value.Height.Should().Be(8);
            readOk = true;
            break;
          }
        }
      }
      readOk.Should().BeTrue("compressed output should contain a readable 2D image HDU");
    }

    TestPaths.DeleteIfExists(src);
    TestPaths.DeleteIfExists(dst);
  }

  [TestMethod]
  public void Open_MissingFile_ReturnsFailure()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");

    string missing = TestPaths.GetTempFile($"missing_{Guid.NewGuid():N}.fits");
    TestPaths.DeleteIfExists(missing);

    var r = FitsFile.Open(missing, readWrite: false);
    r.IsSuccessful.Should().BeFalse();
    r.Error.Should().NotBeNull();
  }
}
