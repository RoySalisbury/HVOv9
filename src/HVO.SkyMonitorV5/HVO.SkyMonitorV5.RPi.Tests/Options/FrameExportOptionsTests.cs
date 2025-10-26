using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Pipeline;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LegacyFitsBitDepth = HVO.SkyMonitorV5.RPi.Options.FitsBitDepth;
using LegacyFitsCompression = HVO.SkyMonitorV5.RPi.Options.FitsCompressionKind;
using P = HVO.SkyMonitorV5.RPi.Pipeline;

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
        Assert.AreEqual(P.FitsBitDepth.U16, options.Raw.ArchiveEncoding.FitsOptions!.BitDepth);
        Assert.AreEqual(FitsImageFormat.Mono, options.Raw.ArchiveEncoding.FitsOptions!.ImageFormat);
        Assert.AreEqual(P.FitsCompression.None, options.Raw.ArchiveEncoding.FitsOptions!.Compression);
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

    [TestMethod]
    public void MigrateFromLegacyFitsOptions_EnableForRaw_MigratesToRawArchiveEncoding()
    {
        // Arrange
        var options = new FrameExportOptions();
        var legacyOptions = new FitsExportOptions
        {
            EnableForRaw = true,
            EnableForProcessed = false,
            BitDepth = LegacyFitsBitDepth.U16,
            Compression = FitsCompressionKind.Rice,
            UnsignedU16 = true,
            WriteChecksum = false
        };

        // Act
#pragma warning disable CS0618 // Type or member is obsolete
        options.MigrateFromLegacyFitsOptions(legacyOptions);
#pragma warning restore CS0618 // Type or member is obsolete

        // Assert
        Assert.IsNotNull(options.Raw.ArchiveEncoding);
        Assert.AreEqual(ImageEncodingFormat.Fits, options.Raw.ArchiveEncoding.Format);
        Assert.IsNotNull(options.Raw.ArchiveEncoding.FitsOptions);
        Assert.AreEqual(P.FitsBitDepth.U16, options.Raw.ArchiveEncoding.FitsOptions!.BitDepth);
        Assert.AreEqual(P.FitsCompression.Rice, options.Raw.ArchiveEncoding.FitsOptions!.Compression);
        Assert.IsTrue(options.Raw.ArchiveEncoding.FitsOptions.UnsignedU16);
        Assert.IsFalse(options.Raw.ArchiveEncoding.FitsOptions.WriteChecksum);
    }

    [TestMethod]
    public void MigrateFromLegacyFitsOptions_EnableForProcessed_MigratesToProcessedArchiveEncoding()
    {
        // Arrange
        var options = new FrameExportOptions();
        var legacyOptions = new FitsExportOptions
        {
            EnableForRaw = false,
            EnableForProcessed = true,
            BitDepth = LegacyFitsBitDepth.U8,
            Compression = FitsCompressionKind.Gzip1,
            UnsignedU16 = false,
            WriteChecksum = true
        };

        // Act
#pragma warning disable CS0618 // Type or member is obsolete
        options.MigrateFromLegacyFitsOptions(legacyOptions);
#pragma warning restore CS0618 // Type or member is obsolete

        // Assert
        Assert.IsNotNull(options.Processed.ArchiveEncoding);
        Assert.AreEqual(ImageEncodingFormat.Fits, options.Processed.ArchiveEncoding.Format);
        Assert.IsNotNull(options.Processed.ArchiveEncoding.FitsOptions);
        Assert.AreEqual(P.FitsBitDepth.U8, options.Processed.ArchiveEncoding.FitsOptions!.BitDepth);
        Assert.AreEqual(P.FitsCompression.Gzip1, options.Processed.ArchiveEncoding.FitsOptions!.Compression);
        Assert.IsFalse(options.Processed.ArchiveEncoding.FitsOptions.UnsignedU16);
        Assert.IsTrue(options.Processed.ArchiveEncoding.FitsOptions.WriteChecksum);
    }

    [TestMethod]
    public void MigrateFromLegacyFitsOptions_BothEnabled_MigratesBothStages()
    {
        // Arrange
        var options = new FrameExportOptions();
        var legacyOptions = new FitsExportOptions
        {
            EnableForRaw = true,
            EnableForProcessed = true,
            BitDepth = LegacyFitsBitDepth.U16,
            Compression = FitsCompressionKind.None,
            UnsignedU16 = true,
            WriteChecksum = true
        };

        // Act
#pragma warning disable CS0618 // Type or member is obsolete
        options.MigrateFromLegacyFitsOptions(legacyOptions);
#pragma warning restore CS0618 // Type or member is obsolete

        // Assert
        Assert.IsNotNull(options.Raw.ArchiveEncoding);
        Assert.AreEqual(ImageEncodingFormat.Fits, options.Raw.ArchiveEncoding.Format);
        Assert.IsNotNull(options.Processed.ArchiveEncoding);
        Assert.AreEqual(ImageEncodingFormat.Fits, options.Processed.ArchiveEncoding.Format);
    }

    [TestMethod]
    public void MigrateFromLegacyFitsOptions_NullLegacyOptions_DoesNotThrow()
    {
        // Arrange
        var options = new FrameExportOptions();

        // Act & Assert
#pragma warning disable CS0618 // Type or member is obsolete
        options.MigrateFromLegacyFitsOptions(default!);
#pragma warning restore CS0618 // Type or member is obsolete
        // No exception should be thrown
    }

    [TestMethod]
    public void MigrateFromLegacyFitsOptions_AllCompressionTypes_ConvertCorrectly()
    {
        // Test each compression type individually
        var compressionTypes = new[]
        {
            (FitsCompressionKind.None, P.FitsCompression.None),
            (FitsCompressionKind.Rice, P.FitsCompression.Rice),
            (FitsCompressionKind.Gzip1, P.FitsCompression.Gzip1),
            (FitsCompressionKind.Gzip2, P.FitsCompression.Gzip2),
            (FitsCompressionKind.HCompress, P.FitsCompression.HCompress)
        };

        foreach (var (legacyCompression, expectedCompression) in compressionTypes)
        {
            // Arrange
            var options = new FrameExportOptions();
            var legacyOptions = new FitsExportOptions
            {
                EnableForRaw = true,
                Compression = legacyCompression
            };

            // Act
#pragma warning disable CS0618 // Type or member is obsolete
            options.MigrateFromLegacyFitsOptions(legacyOptions);
#pragma warning restore CS0618 // Type or member is obsolete

            // Assert
            Assert.IsNotNull(options.Raw.ArchiveEncoding.FitsOptions);
            Assert.AreEqual(expectedCompression, options.Raw.ArchiveEncoding.FitsOptions!.Compression,
                $"Failed to convert {legacyCompression} to {expectedCompression}");
        }
    }

    [TestMethod]
    public void MigrateFromLegacyFitsOptions_AllBitDepths_ConvertCorrectly()
    {
        // Test each bit depth
        var bitDepths = new[]
        {
            (LegacyFitsBitDepth.U8, P.FitsBitDepth.U8),
            (LegacyFitsBitDepth.U16, P.FitsBitDepth.U16)
        };

        foreach (var (legacyBitDepth, expectedBitDepth) in bitDepths)
        {
            // Arrange
            var options = new FrameExportOptions();
            var legacyOptions = new FitsExportOptions
            {
                EnableForRaw = true,
                BitDepth = legacyBitDepth
            };

            // Act
#pragma warning disable CS0618 // Type or member is obsolete
            options.MigrateFromLegacyFitsOptions(legacyOptions);
#pragma warning restore CS0618 // Type or member is obsolete

            // Assert
            Assert.IsNotNull(options.Raw.ArchiveEncoding.FitsOptions);
            Assert.AreEqual(expectedBitDepth, options.Raw.ArchiveEncoding.FitsOptions!.BitDepth,
                $"Failed to convert {legacyBitDepth} to {expectedBitDepth}");
        }
    }
}
