using System;
using System.IO;
using FluentAssertions;
using HVO.Astronomy.CFITSIO.Interop;
using HVO.Astronomy.CFITSIO.Tests.Helpers;

namespace HVO.Astronomy.CFITSIO.Tests;

[TestClass]
public class FitsFileErrorAndEdgeTests
{
  [TestMethod]
  public void MoveToHdu_InvalidIndex_ReturnsFailure()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");
    string path = TestPaths.GetTempFile($"move_invalid_{Guid.NewGuid():N}.fits");
    TestPaths.DeleteIfExists(path);

    var c = FitsFile.Create("!" + path);
    using var fits = c.Value;
    // Create a single HDU
    FitsImage.WriteU8(fits, 2, 2, new byte[4]).IsSuccessful.Should().BeTrue();

    var r = fits.MoveToHdu(999); // absurdly high
    r.IsSuccessful.Should().BeFalse();
    r.Error.Should().NotBeNull();

    fits.Dispose();
    TestPaths.DeleteIfExists(path);
  }

  [TestMethod]
  public void MoveBy_InvalidRelativeOffset_ReturnsFailure()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");
    string path = TestPaths.GetTempFile($"moverel_invalid_{Guid.NewGuid():N}.fits");
    TestPaths.DeleteIfExists(path);

    var c = FitsFile.Create("!" + path);
    using var fits = c.Value;
    FitsImage.WriteU8(fits, 2, 2, new byte[4]).IsSuccessful.Should().BeTrue();

    // Currently on HDU 1. Move back beyond start
    var r = fits.MoveBy(-5);
    r.IsSuccessful.Should().BeFalse();
    r.Error.Should().NotBeNull();

    fits.Dispose();
    TestPaths.DeleteIfExists(path);
  }

  [TestMethod]
  public void CreateImageHdu_WithEmptyAxes_ReturnsArgumentException()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");
    string path = TestPaths.GetTempFile($"empty_axes_{Guid.NewGuid():N}.fits");
    TestPaths.DeleteIfExists(path);

    var c = FitsFile.Create("!" + path);
    using var fits = c.Value;

    var r = fits.CreateImageHdu(CFitsIO.BYTE_IMG, Array.Empty<long>());
    r.IsSuccessful.Should().BeFalse();
    r.Error.Should().BeOfType<ArgumentException>();

    fits.Dispose();
    TestPaths.DeleteIfExists(path);
  }

  [TestMethod]
  public void CreateImageHdu_WithNegativeAxis_ReturnsFailure()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");
    string path = TestPaths.GetTempFile($"neg_axis_{Guid.NewGuid():N}.fits");
    TestPaths.DeleteIfExists(path);

    var c = FitsFile.Create("!" + path);
    using var fits = c.Value;

    var r = fits.CreateImageHdu(CFitsIO.BYTE_IMG, -1, 10);
    r.IsSuccessful.Should().BeFalse();
    r.Error.Should().NotBeNull();

    fits.Dispose();
    TestPaths.DeleteIfExists(path);
  }
}
