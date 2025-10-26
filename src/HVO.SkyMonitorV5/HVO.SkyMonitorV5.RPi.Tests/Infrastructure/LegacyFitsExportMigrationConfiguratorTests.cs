using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace HVO.SkyMonitorV5.RPi.Tests.Infrastructure;

[TestClass]
public sealed class LegacyFitsExportMigrationConfiguratorTests
{
  [TestMethod]
  public void PostConfigure_WhenLegacyOptionsNotEnabled_DoesNotMigrate()
  {
    // Arrange
    var legacyOptions = new FitsExportOptions
    {
      EnableForRaw = false,
      EnableForProcessed = false,
      BitDepth = FitsBitDepth.U16,
      Compression = FitsCompressionKind.Rice
    };

    var legacyMonitor = new Mock<IOptionsMonitor<FitsExportOptions>>();
    legacyMonitor.SetupGet(m => m.CurrentValue).Returns(legacyOptions);

    var configurator = new LegacyFitsExportMigrationConfigurator(
        legacyMonitor.Object,
        NullLogger<LegacyFitsExportMigrationConfigurator>.Instance);

    var options = new FrameExportOptions();

    // Act
    configurator.PostConfigure(string.Empty, options);

    // Assert
    // Options should remain unmodified (default JPEG for processed)
    Assert.AreEqual(global::HVO.SkyMonitorV5.RPi.Pipeline.ImageEncodingFormat.Jpeg, options.Processed.ArchiveEncoding.Format);
  }

  [TestMethod]
  public void PostConfigure_WhenLegacyEnabledForRaw_MigratesRawStage()
  {
    // Arrange
    var legacyOptions = new FitsExportOptions
    {
      EnableForRaw = true,
      EnableForProcessed = false,
      BitDepth = FitsBitDepth.U16,
      Compression = FitsCompressionKind.Rice,
      UnsignedU16 = true,
      WriteChecksum = true
    };

    var legacyMonitor = new Mock<IOptionsMonitor<FitsExportOptions>>();
    legacyMonitor.SetupGet(m => m.CurrentValue).Returns(legacyOptions);

    var configurator = new LegacyFitsExportMigrationConfigurator(
        legacyMonitor.Object,
        NullLogger<LegacyFitsExportMigrationConfigurator>.Instance);

    var options = new FrameExportOptions();

    // Act
    configurator.PostConfigure(string.Empty, options);

    // Assert
    Assert.IsNotNull(options.Raw);
    Assert.AreEqual(global::HVO.SkyMonitorV5.RPi.Pipeline.ImageEncodingFormat.Fits, options.Raw.ArchiveEncoding.Format);
    Assert.IsNotNull(options.Raw.ArchiveEncoding.FitsOptions);
    Assert.IsTrue(options.Raw.ArchiveEncoding.FitsOptions.BitDepth == global::HVO.SkyMonitorV5.RPi.Pipeline.FitsBitDepth.U16);
    Assert.IsTrue(options.Raw.ArchiveEncoding.FitsOptions.Compression == global::HVO.SkyMonitorV5.RPi.Pipeline.FitsCompression.Rice);
    Assert.AreEqual(true, options.Raw.ArchiveEncoding.FitsOptions.UnsignedU16);
    Assert.AreEqual(true, options.Raw.ArchiveEncoding.FitsOptions.WriteChecksum);
  }

  [TestMethod]
  public void PostConfigure_WhenLegacyEnabledForProcessed_MigratesProcessedStage()
  {
    // Arrange
    var legacyOptions = new FitsExportOptions
    {
      EnableForRaw = false,
      EnableForProcessed = true,
      BitDepth = FitsBitDepth.U8,
      Compression = FitsCompressionKind.Gzip1,
      UnsignedU16 = false,
      WriteChecksum = false
    };

    var legacyMonitor = new Mock<IOptionsMonitor<FitsExportOptions>>();
    legacyMonitor.SetupGet(m => m.CurrentValue).Returns(legacyOptions);

    var configurator = new LegacyFitsExportMigrationConfigurator(
        legacyMonitor.Object,
        NullLogger<LegacyFitsExportMigrationConfigurator>.Instance);

    var options = new FrameExportOptions();

    // Act
    configurator.PostConfigure(string.Empty, options);

    // Assert
    Assert.IsNotNull(options.Processed);
    Assert.AreEqual(global::HVO.SkyMonitorV5.RPi.Pipeline.ImageEncodingFormat.Fits, options.Processed.ArchiveEncoding.Format);
    Assert.IsNotNull(options.Processed.ArchiveEncoding.FitsOptions);
    Assert.IsFalse(options.Processed.ArchiveEncoding.FitsOptions.BitDepth != global::HVO.SkyMonitorV5.RPi.Pipeline.FitsBitDepth.U8);
    Assert.IsFalse(options.Processed.ArchiveEncoding.FitsOptions.Compression != global::HVO.SkyMonitorV5.RPi.Pipeline.FitsCompression.Gzip1);
    Assert.AreEqual(false, options.Processed.ArchiveEncoding.FitsOptions.UnsignedU16);
    Assert.AreEqual(false, options.Processed.ArchiveEncoding.FitsOptions.WriteChecksum);
  }

  [TestMethod]
  public void PostConfigure_WhenLegacyEnabledForBothStages_MigratesBoth()
  {
    // Arrange
    var legacyOptions = new FitsExportOptions
    {
      EnableForRaw = true,
      EnableForProcessed = true,
      BitDepth = FitsBitDepth.U16,
      Compression = FitsCompressionKind.None,
      UnsignedU16 = true,
      WriteChecksum = true
    };

    var legacyMonitor = new Mock<IOptionsMonitor<FitsExportOptions>>();
    legacyMonitor.SetupGet(m => m.CurrentValue).Returns(legacyOptions);

    var configurator = new LegacyFitsExportMigrationConfigurator(
        legacyMonitor.Object,
        NullLogger<LegacyFitsExportMigrationConfigurator>.Instance);

    var options = new FrameExportOptions();

    // Act
    configurator.PostConfigure(string.Empty, options);

    // Assert - Raw
    Assert.IsNotNull(options.Raw);
    Assert.AreEqual(global::HVO.SkyMonitorV5.RPi.Pipeline.ImageEncodingFormat.Fits, options.Raw.ArchiveEncoding.Format);
    Assert.IsNotNull(options.Raw.ArchiveEncoding.FitsOptions);

    // Assert - Processed
    Assert.IsNotNull(options.Processed);
    Assert.AreEqual(global::HVO.SkyMonitorV5.RPi.Pipeline.ImageEncodingFormat.Fits, options.Processed.ArchiveEncoding.Format);
    Assert.IsNotNull(options.Processed.ArchiveEncoding.FitsOptions);
    Assert.IsFalse(options.Processed.ArchiveEncoding.FitsOptions.Compression != global::HVO.SkyMonitorV5.RPi.Pipeline.FitsCompression.None);
  }

  [TestMethod]
  public void PostConfigure_WithNullOptions_DoesNotThrow()
  {
    // Arrange
    var legacyOptions = new FitsExportOptions { EnableForRaw = true };
    var legacyMonitor = new Mock<IOptionsMonitor<FitsExportOptions>>();
    legacyMonitor.SetupGet(m => m.CurrentValue).Returns(legacyOptions);

    var configurator = new LegacyFitsExportMigrationConfigurator(
        legacyMonitor.Object,
        NullLogger<LegacyFitsExportMigrationConfigurator>.Instance);

    // Act & Assert - should not throw
    configurator.PostConfigure(string.Empty, null!);
  }
}
