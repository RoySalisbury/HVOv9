using HVO.SkyMonitorV5.RPi.Options;
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
}
