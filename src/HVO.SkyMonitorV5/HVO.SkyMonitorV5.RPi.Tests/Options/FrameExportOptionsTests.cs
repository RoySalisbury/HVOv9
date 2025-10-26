using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Pipeline;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HVO.SkyMonitorV5.RPi.Tests.Options;

[TestClass]
public sealed class FrameExportOptionsTests
{
    [TestMethod]
    public void Normalize_AssignsStageSpecificPayloadScopeDefaults()
    {
        var options = new FrameExportOptions
        {
            Raw = new FrameExportStageOptions(),
            Processed = new FrameExportStageOptions()
        };

        options.Normalize();

        Assert.AreEqual(FrameExportPayloadScope.ArchiveOnly, options.Raw.PayloadScope);
        Assert.AreEqual(FrameExportPayloadScope.ArchiveOnly, options.Processed.PayloadScope, "Processed exports now default to ArchiveOnly instead of DeliveryOnly");
    }

    [TestMethod]
    public void Normalize_PreservesConfiguredPayloadScope()
    {
        var options = new FrameExportOptions
        {
            Raw = new FrameExportStageOptions { PayloadScope = FrameExportPayloadScope.DeliveryOnly },
            Processed = new FrameExportStageOptions { PayloadScope = FrameExportPayloadScope.ArchiveAndDelivery }
        };

        options.Normalize();

        Assert.AreEqual(FrameExportPayloadScope.DeliveryOnly, options.Raw.PayloadScope);
        Assert.AreEqual(FrameExportPayloadScope.ArchiveAndDelivery, options.Processed.PayloadScope);
    }

    [TestMethod]
    public void Normalize_RawStage_NullArchiveEncoding_DefaultsToFits()
    {
        // Arrange
        var options = new FrameExportOptions
        {
            Raw = new FrameExportStageOptions { ArchiveEncoding = null! }
        };

        // Act
        options.Normalize();

        // Assert
        Assert.IsNotNull(options.Raw.ArchiveEncoding);
        Assert.AreEqual(ImageEncodingFormat.Fits, options.Raw.ArchiveEncoding.Format);
        Assert.AreEqual(100, options.Raw.ArchiveEncoding.Quality);
        Assert.IsNotNull(options.Raw.ArchiveEncoding.FitsOptions);
        Assert.AreEqual(FitsBitDepth.U16, options.Raw.ArchiveEncoding.FitsOptions!.BitDepth);
        Assert.AreEqual(FitsImageFormat.Mono, options.Raw.ArchiveEncoding.FitsOptions!.ImageFormat);
        Assert.AreEqual(FitsCompression.None, options.Raw.ArchiveEncoding.FitsOptions!.Compression);
    }

    [TestMethod]
    public void Normalize_ProcessedStage_NullArchiveEncoding_DefaultsToJpeg()
    {
        // Arrange
        var options = new FrameExportOptions
        {
            Processed = new FrameExportStageOptions { ArchiveEncoding = null! }
        };

        // Act
        options.Normalize();

        // Assert
        Assert.IsNotNull(options.Processed.ArchiveEncoding);
        Assert.AreEqual(ImageEncodingFormat.Jpeg, options.Processed.ArchiveEncoding.Format);
        Assert.AreEqual(95, options.Processed.ArchiveEncoding.Quality);
        Assert.IsNull(options.Processed.ArchiveEncoding.FitsOptions);
    }

    [TestMethod]
    public void Normalize_PreservesExistingArchiveEncoding()
    {
        // Arrange
        var customEncoding = new ImageEncodingSettings(ImageEncodingFormat.Png, 100);
        var options = new FrameExportOptions
        {
            Raw = new FrameExportStageOptions { ArchiveEncoding = customEncoding },
            Processed = new FrameExportStageOptions { ArchiveEncoding = customEncoding }
        };

        // Act
        options.Normalize();

        // Assert
        Assert.AreEqual(ImageEncodingFormat.Png, options.Raw.ArchiveEncoding.Format);
        Assert.AreEqual(ImageEncodingFormat.Png, options.Processed.ArchiveEncoding.Format);
    }
}
