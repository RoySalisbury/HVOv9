using System;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Exports;
using HVO.SkyMonitorV5.RPi.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Tests.Exports;

[TestClass]
public sealed class FrameExportMetadataBuilderTests
{
    [TestMethod]
    public void FromRaw_ComputesFullPipelineDuration()
    {
        var rig = RigPresets.MockAsi174_Fujinon;
        var exposure = new ExposureSettings(500, 100, false, false);
        using var bitmap = new SKBitmap(width: 8, height: 8);
        var capture = new CapturedImage(
            Guid.NewGuid(),
            bitmap,
            DateTimeOffset.UtcNow,
            exposure,
            null);

        var now = DateTimeOffset.UtcNow;
        var metadata = FrameExportMetadataBuilder.FromRaw(
            capture,
            rig,
            now,
            queueLatencyMilliseconds: 12.5,
            processingMilliseconds: null,
            rawImageDescriptor: null);

        Assert.AreEqual(1, metadata.FramesStacked, "Raw exports should report single frame stack.");
    Assert.AreEqual(exposure.ExposureMilliseconds, metadata.IntegrationMilliseconds, "Integration should match exposure duration.");
    Assert.IsNull(metadata.ProcessingMilliseconds, "Raw export should not record processing duration by default.");
    Assert.IsNotNull(metadata.FullPipelineMilliseconds, "Full pipeline duration should be calculated for raw exports.");
    var rawFullPipeline = metadata.FullPipelineMilliseconds!.Value;
    Assert.AreEqual(12.5 + exposure.ExposureMilliseconds, rawFullPipeline, 1e-6, "Full pipeline duration should combine exposure and queue latency.");
    }

    [TestMethod]
    public void FromProcessed_ComputesFullPipelineDuration()
    {
        var rig = RigPresets.MockAsi174_Fujinon;
        var exposure = new ExposureSettings(750, 180, false, false);
        var processed = new ProcessedFrame(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            exposure,
            ImageBytes: new byte[] { 1, 2, 3 },
            ContentType: "image/png",
            FramesStacked: 4,
            IntegrationMilliseconds: 3000,
            AppliedFilters: Array.Empty<string>(),
            ProcessingMilliseconds: 0);

        var stageTimestamp = DateTimeOffset.UtcNow;
        var metadata = FrameExportMetadataBuilder.FromProcessed(
            processed,
            context: null,
            rig,
            stageTimestamp,
            queueLatencyMilliseconds: 25.0,
            processingMilliseconds: 175.5);

        Assert.AreEqual(4, metadata.FramesStacked, "Processed export should carry stacked frame count.");
        Assert.AreEqual(3000, metadata.IntegrationMilliseconds, "Integration should reflect stacked exposure sum.");
        Assert.AreEqual(175.5, metadata.ProcessingMilliseconds, "Processing duration should match pipeline runtime.");
        Assert.IsNotNull(metadata.FullPipelineMilliseconds, "Full pipeline duration should be populated for processed exports.");
        var processedFullPipeline = metadata.FullPipelineMilliseconds!.Value;
    Assert.AreEqual(950.5, processedFullPipeline, 1e-6, "Full pipeline duration should reflect single-frame exposure plus latencies.");
    }
}
