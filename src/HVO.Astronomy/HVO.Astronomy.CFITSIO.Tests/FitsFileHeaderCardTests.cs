using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using HVO.Astronomy.CFITSIO.Interop;
using HVO.Astronomy.CFITSIO.Tests.Helpers;

namespace HVO.Astronomy.CFITSIO.Tests;

[TestClass]
public class FitsFileHeaderCardTests
{
  [TestMethod]
  public void WriteHeaderCard_Then_ReadAllHeaderCards_ShouldContainIt()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");

    string path = TestPaths.GetTempFile($"card_{Guid.NewGuid():N}.fits");
    TestPaths.DeleteIfExists(path);

    var create = FitsFile.Create("!" + path);
    create.IsSuccessful.Should().BeTrue(create.Error?.ToString());
    using var fits = create.Value;

    // Create small image HDU so we can write header cards on a valid HDU
    fits.CreateImageHdu(CFitsIO.BYTE_IMG, 2, 2).IsSuccessful.Should().BeTrue();

    // Write a raw 80-char card
    string keyword = "HVOXYZZY";
    string card = $"{keyword,-8}= 'alpha'  / sample raw card";
    card.Length.Should().BeLessThanOrEqualTo(80); // CFITSIO will pad as needed
    var wr = fits.WriteHeaderCard(card);
    wr.IsSuccessful.Should().BeTrue(wr.Error?.ToString());

    // Read all header cards and ensure our card is present
    var all = fits.ReadAllHeaderCards();
    all.IsSuccessful.Should().BeTrue(all.Error?.ToString());
    all.Value.Any(c => c.StartsWith(keyword)).Should().BeTrue("expected to find our raw header card");

    // Clean up
    fits.Dispose();
    File.Exists(path).Should().BeTrue();
    TestPaths.DeleteIfExists(path);
  }

  [TestMethod]
  public void WriteAndRead_Typed_Keywords_All_Types()
  {
    if (!NativeAvailability.HasCFitsIO) Assert.Inconclusive("CFITSIO native library not available on this platform.");

    string path = TestPaths.GetTempFile($"typedkeys_{Guid.NewGuid():N}.fits");
    TestPaths.DeleteIfExists(path);

    var create = FitsFile.Create("!" + path);
    using var fits = create.Value;

    fits.CreateImageHdu(CFitsIO.USHORT_IMG, 3, 3).IsSuccessful.Should().BeTrue();

    // String
    fits.WriteKeyString("OBSERVER", "unit-test", "observer").IsSuccessful.Should().BeTrue();
    fits.TryGetKeyString("OBSERVER").Value.Should().Be("unit-test");

    // Int32
    fits.WriteKeyInt32("SEQ", 123, "sequence").IsSuccessful.Should().BeTrue();
    fits.TryGetKeyInt32("SEQ").Value.Should().Be(123);

    // Int64 (stored as string under the hood)
    long big = 9876543210123L;
    fits.WriteKeyInt64("EPOCH_MS", big, "epoch").IsSuccessful.Should().BeTrue();
    fits.TryGetKeyInt64("EPOCH_MS").Value.Should().Be(big);

    // Double with explicit decimals
    fits.WriteKeyDouble("GAIN", 2.5, decimals: 2, comment: "gain").IsSuccessful.Should().BeTrue();
    fits.TryGetKeyDouble("GAIN").Value.Should().BeApproximately(2.5, 1e-12);

    // Boolean
    fits.WriteKeyBoolean("FOCUS_OK", true, "focus").IsSuccessful.Should().BeTrue();
    fits.TryGetKeyBoolean("FOCUS_OK").Value.Should().BeTrue();

    // Verify read-all also includes some of these keywords
    var all = fits.ReadAllHeaderCards();
    all.IsSuccessful.Should().BeTrue();
    all.Value.Any(c => c.StartsWith("OBSERVER")).Should().BeTrue();
    all.Value.Any(c => c.StartsWith("SEQ")).Should().BeTrue();

    fits.Dispose();
    TestPaths.DeleteIfExists(path);
  }
}
