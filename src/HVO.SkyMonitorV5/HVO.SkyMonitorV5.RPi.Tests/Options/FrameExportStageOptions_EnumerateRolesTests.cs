using System.Linq;
using HVO.SkyMonitorV5.RPi.Exports;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Pipeline;

namespace HVO.SkyMonitorV5.RPi.Tests.Options;

[TestClass]
public sealed class FrameExportStageOptions_EnumerateRolesTests
{
  [TestMethod]
  public void EnumerateRoles_ArchiveOnly_ReturnsArchive()
  {
    var stageOptions = new FrameExportStageOptions
    {
      PayloadScope = FrameExportPayloadScope.ArchiveOnly,
      ArchiveEncoding = new ImageEncodingSettings(ImageEncodingFormat.Fits, 100),
      DeliveryEncoding = new ImageEncodingSettings(ImageEncodingFormat.Jpeg, 80)
    };

    var roles = stageOptions.EnumerateRoles().ToArray();

    Assert.HasCount(1, roles, "ArchiveOnly should yield exactly one role.");
    Assert.AreEqual(FrameExportPayloadRole.Archive, roles[0], "Role should be Archive.");
  }

  [TestMethod]
  public void EnumerateRoles_DeliveryOnly_ReturnsDelivery()
  {
    var stageOptions = new FrameExportStageOptions
    {
      PayloadScope = FrameExportPayloadScope.DeliveryOnly,
      ArchiveEncoding = new ImageEncodingSettings(ImageEncodingFormat.Fits, 100),
      DeliveryEncoding = new ImageEncodingSettings(ImageEncodingFormat.Jpeg, 80)
    };

    var roles = stageOptions.EnumerateRoles().ToArray();

    Assert.HasCount(1, roles, "DeliveryOnly should yield exactly one role.");
    Assert.AreEqual(FrameExportPayloadRole.Delivery, roles[0], "Role should be Delivery.");
  }

  [TestMethod]
  public void EnumerateRoles_ArchiveAndDelivery_ReturnsBoth()
  {
    var stageOptions = new FrameExportStageOptions
    {
      PayloadScope = FrameExportPayloadScope.ArchiveAndDelivery,
      ArchiveEncoding = new ImageEncodingSettings(ImageEncodingFormat.Png, 100),
      DeliveryEncoding = new ImageEncodingSettings(ImageEncodingFormat.Jpeg, 75)
    };

    var roles = stageOptions.EnumerateRoles().ToArray();

    Assert.HasCount(2, roles, "ArchiveAndDelivery should yield both roles.");
    CollectionAssert.Contains(roles, FrameExportPayloadRole.Archive, "Roles should include Archive.");
    CollectionAssert.Contains(roles, FrameExportPayloadRole.Delivery, "Roles should include Delivery.");
  }

  [TestMethod]
  public void EnumerateRoles_Unspecified_ReturnsEmpty()
  {
    var stageOptions = new FrameExportStageOptions
    {
      PayloadScope = FrameExportPayloadScope.Unspecified,
      ArchiveEncoding = new ImageEncodingSettings(ImageEncodingFormat.Jpeg, 95)
    };

    var roles = stageOptions.EnumerateRoles().ToArray();

    Assert.IsEmpty(roles, "Unspecified scope should yield no roles.");
  }

  [TestMethod]
  public void EnumerateRoles_RawDefaults_ReturnsArchive()
  {
    var options = new FrameExportOptions { Raw = new FrameExportStageOptions() };
    options.Normalize();

    var roles = options.Raw.EnumerateRoles().ToArray();

    Assert.HasCount(1, roles, "Raw stage default should be ArchiveOnly.");
    Assert.AreEqual(FrameExportPayloadRole.Archive, roles[0], "Default role should be Archive.");
  }

  [TestMethod]
  public void EnumerateRoles_ProcessedDefaults_ReturnsArchive()
  {
    var options = new FrameExportOptions { Processed = new FrameExportStageOptions() };
    options.Normalize();

    var roles = options.Processed.EnumerateRoles().ToArray();

    Assert.HasCount(1, roles, "Processed stage default should be ArchiveOnly.");
    Assert.AreEqual(FrameExportPayloadRole.Archive, roles[0], "Default role should be Archive.");
  }
}
