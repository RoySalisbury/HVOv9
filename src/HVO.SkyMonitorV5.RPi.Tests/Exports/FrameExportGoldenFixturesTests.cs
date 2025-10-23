using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Exports;
using HVO.SkyMonitorV5.RPi.Exports.Sinks;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.ImageHistory;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Services;
using HVO.SkyMonitorV5.RPi.Skia;
using HVO.SkyMonitorV5.RPi.Telemetry;
using HVO.SkyMonitorV5.RPi.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Tests.Exports;

[TestClass]
public sealed class FrameExportGoldenFixturesTests
{
    [TestMethod]
    public void RawAndProcessedPayloads_StayAlignedWithGoldenHashes()
    {
        var info = new SKImageInfo(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);

        for (var y = 0; y < info.Height; y++)
        {
            for (var x = 0; x < info.Width; x++)
            {
                var red = (byte)((x + 1) * 32);
                var green = (byte)((y + 1) * 48);
                var blue = (byte)(((x + y) + 1) * 40);
                bitmap.SetPixel(x, y, new SKColor(red, green, blue, 255));
            }
        }

        using var image = SKImage.FromBitmap(bitmap);

        var frameId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeffffffff");
        var timestamp = new DateTimeOffset(2025, 10, 16, 0, 0, 0, TimeSpan.Zero);
        var exposure = new ExposureSettings(ExposureMilliseconds: 1000, Gain: 200, AutoExposure: false, AutoGain: false);
        var rig = RigPresets.MockAsi174_Fujinon;
        var stageTimestampUtc = timestamp.AddMinutes(1);

        var capture = new CapturedImage(frameId, bitmap, timestamp, exposure, Context: null)
        {
            ImmutableImage = image
        };

        var rawSuccess = SkiaRawFrameHelper.TryCreateRawPayload(image, out var rawPayload, out var rawDescriptor);
        Assert.IsTrue(rawSuccess, "Expected raw payload extraction to succeed.");
        Assert.IsNotNull(rawDescriptor, "Raw descriptor should accompany payload.");

        var rawHash = Convert.ToHexString(SHA256.HashData(rawPayload));
        const string expectedRawHash = "062DBE6DC896E8CD19F3B6C3EAD6B5FA12D86860AED68ED2D8E412A85B8C6C89";
        Assert.AreEqual(expectedRawHash, rawHash, "Raw payload hash should remain stable across runs.");

        var rawMetadata = FrameExportMetadataBuilder.FromRaw(
            capture,
            rig,
            stageTimestampUtc,
            queueLatencyMilliseconds: 5.0,
            processingMilliseconds: null,
            rawImageDescriptor: rawDescriptor,
            payloadContentType: SkiaRawFrameHelper.RawContentType,
            payloadExtension: SkiaRawFrameHelper.RawFileExtension);

        var rawEnvelope = new FrameExportEnvelope(
            frameId,
            FrameExportStage.Raw,
            rawMetadata,
            new ReadOnlyMemory<byte>(rawPayload),
            SkiaRawFrameHelper.RawContentType,
            SkiaRawFrameHelper.RawFileExtension);

        var encoding = new ImageEncodingSettings(ImageEncodingFormat.Png, 100);
        var processedFrame = new ProcessedFrame(
            frameId,
            timestamp,
            exposure,
            encoding,
            ImageEncodingUtilities.ToContentType(encoding.Format),
            ImageEncodingUtilities.ToFileExtension(encoding.Format),
            FramesStacked: 1,
            IntegrationMilliseconds: exposure.ExposureMilliseconds,
            AppliedFilters: Array.Empty<string>(),
            ProcessingMilliseconds: 0,
            ImmutableImage: image);

        var encoder = new ProcessedFrameEncoder(NullLogger<ProcessedFrameEncoder>.Instance);
        var delivery = encoder.Encode(processedFrame);

        var processedHash = Convert.ToHexString(SHA256.HashData(delivery.Payload.Span));
        const string expectedProcessedHash = "C22A5C3A47ACA7F5A8E2A146E34ED5DDF5D3F68AAB129054A6758C36C225D196";
        Assert.AreEqual(expectedProcessedHash, processedHash, "Processed PNG hash should remain stable across runs.");

        var processedMetadata = FrameExportMetadataBuilder.FromProcessed(
            processedFrame,
            context: null,
            rig,
            stageTimestampUtc,
            queueLatencyMilliseconds: 5.0,
            processingMilliseconds: 0.0,
            payloadContentType: delivery.ContentType,
            payloadExtension: delivery.FileExtension);

        var processedEnvelope = new FrameExportEnvelope(
            frameId,
            FrameExportStage.Processed,
            processedMetadata,
            delivery.Payload,
            delivery.ContentType,
            delivery.FileExtension);

        Assert.AreEqual(rawEnvelope.FrameId, processedEnvelope.FrameId, "Frame identifiers should align across archive and delivery payloads.");
        Assert.AreEqual(rawEnvelope.Metadata.StageTimestampUtc, processedEnvelope.Metadata.StageTimestampUtc, "Stage timestamps should remain consistent.");
        Assert.AreEqual(SkiaRawFrameHelper.RawContentType, rawEnvelope.Metadata.PayloadContentType, "Raw metadata should advertise raw content type.");
        Assert.AreEqual(SkiaRawFrameHelper.RawFileExtension, rawEnvelope.Metadata.PayloadExtension, "Raw metadata should advertise raw file extension.");
        Assert.AreEqual(delivery.ContentType, processedEnvelope.Metadata.PayloadContentType, "Processed metadata should advertise delivery content type.");
        Assert.AreEqual(delivery.FileExtension, processedEnvelope.Metadata.PayloadExtension, "Processed metadata should advertise delivery file extension.");
    }

    [TestMethod]
    public async Task FilesystemSink_DualScopeExport_MatchesGoldenFixturesAsync()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), FormattableString.Invariant($"hvo-fixtures-{Guid.NewGuid():N}"));
        Directory.CreateDirectory(rootPath);

        FrameExportDispatcher? dispatcher = null;

        using var meterFactory = new TestMeterFactory();
        using var metrics = new FrameExportMetrics(meterFactory);

        var stageTimestampUtc = new DateTimeOffset(2025, 10, 16, 0, 1, 0, TimeSpan.Zero);
        var clock = new Mock<IObservatoryClock>(MockBehavior.Strict);
        clock.SetupGet(c => c.UtcNow).Returns(() => stageTimestampUtc);
        clock.SetupGet(c => c.LocalNow).Returns(() => stageTimestampUtc);
        clock.SetupGet(c => c.TimeZone).Returns(TimeZoneInfo.Utc);
        clock.SetupGet(c => c.TimeZoneDisplayName).Returns("UTC");
        clock.Setup(c => c.ToLocal(It.IsAny<DateTimeOffset>())).Returns<DateTimeOffset>(value => value);
        clock.Setup(c => c.GetZoneLabel(It.IsAny<DateTimeOffset>())).Returns("UTC");

        try
        {
            var options = new FrameExportOptions();
            options.Raw.Enabled = true;
            options.Raw.PayloadScope = FrameExportPayloadScope.ArchiveAndDelivery;
            options.Raw.Filesystem.Add(new FilesystemFrameExportSinkOptions
            {
                Enabled = true,
                RootPath = rootPath,
                Prefix = "fixtures/raw",
                IncludeMetadataManifest = true
            });

            options.Processed.Enabled = true;
            options.Processed.PayloadScope = FrameExportPayloadScope.ArchiveAndDelivery;
            options.Processed.Filesystem.Add(new FilesystemFrameExportSinkOptions
            {
                Enabled = true,
                RootPath = rootPath,
                Prefix = "fixtures/processed",
                IncludeMetadataManifest = true
            });

            options.Normalize();

            using var optionsMonitor = new TestOptionsMonitor<FrameExportOptions>(options);

            var rawSink = new FilesystemFrameExportSink(
                FrameExportStage.Raw,
                optionsMonitor,
                NullLogger<FilesystemFrameExportSink>.Instance);

            var processedSink = new FilesystemFrameExportSink(
                FrameExportStage.Processed,
                optionsMonitor,
                NullLogger<FilesystemFrameExportSink>.Instance);

            var dispatcherOptions = Microsoft.Extensions.Options.Options.Create(new FrameExportDispatcherOptions
            {
                ChannelCapacity = 8,
                MaxConcurrency = 2,
                DrainTimeout = TimeSpan.FromSeconds(5)
            });

            dispatcher = new FrameExportDispatcher(
                dispatcherOptions,
                new IFrameExportSink[] { rawSink, processedSink },
                clock.Object,
                telemetryRecorder: null,
                metrics,
                retryQueue: null,
                NullLogger<FrameExportDispatcher>.Instance);

            await dispatcher.StartAsync(CancellationToken.None).ConfigureAwait(false);

            var rig = RigPresets.MockAsi174_Fujinon;
            var exposure = new ExposureSettings(ExposureMilliseconds: 1000, Gain: 200, AutoExposure: false, AutoGain: false);
            var frameId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeffffffff");
            var captureTimestamp = new DateTimeOffset(2025, 10, 16, 0, 0, 0, TimeSpan.Zero);

            var info = new SKImageInfo(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var rawBitmap = new SKBitmap(info);

            for (var y = 0; y < info.Height; y++)
            {
                for (var x = 0; x < info.Width; x++)
                {
                    var red = (byte)((x + 1) * 32);
                    var green = (byte)((y + 1) * 48);
                    var blue = (byte)(((x + y) + 1) * 40);
                    rawBitmap.SetPixel(x, y, new SKColor(red, green, blue, 255));
                }
            }

            using var immutableImage = SKImage.FromBitmap(rawBitmap);
            using var stackedBitmap = rawBitmap.Copy();
            using var originalBitmap = rawBitmap.Copy();

            var capture = new CapturedImage(frameId, rawBitmap, captureTimestamp, exposure, Context: null)
            {
                ImmutableImage = immutableImage
            };

            var rawSuccess = SkiaRawFrameHelper.TryCreateRawPayload(immutableImage, out var rawPayloadMemory, out var rawDescriptor);
            Assert.IsTrue(rawSuccess, "Expected raw payload extraction to succeed for golden fixture.");
            var expectedRawPayload = rawPayloadMemory.ToArray();

            var encoder = new ProcessedFrameEncoder(NullLogger<ProcessedFrameEncoder>.Instance);
            var processedEncoding = new ImageEncodingSettings(ImageEncodingFormat.Png, 100);

            var processedFrame = new ProcessedFrame(
                frameId,
                captureTimestamp,
                exposure,
                processedEncoding,
                ImageEncodingUtilities.ToContentType(processedEncoding.Format),
                ImageEncodingUtilities.ToFileExtension(processedEncoding.Format),
                FramesStacked: 1,
                IntegrationMilliseconds: exposure.ExposureMilliseconds,
                AppliedFilters: Array.Empty<string>(),
                ProcessingMilliseconds: 0,
                ImmutableImage: immutableImage);

            var expectedDelivery = encoder.Encode(processedFrame);
            var expectedDeliveryPayload = expectedDelivery.Payload.ToArray();

            var stackResult = new FrameStackResult(
                frameId,
                stackedBitmap,
                originalBitmap,
                captureTimestamp,
                exposure,
                Context: null,
                FramesStacked: 1,
                IntegrationMilliseconds: exposure.ExposureMilliseconds)
            {
                StackedImmutableImage = immutableImage,
                OriginalImmutableImage = immutableImage
            };

            var featureOptions = new Mock<IOptionsMonitor<SkiaPipelineFeatureOptions>>();
            featureOptions.SetupGet(o => o.CurrentValue).Returns(new SkiaPipelineFeatureOptions());
            var featureMonitor = new Mock<ISkiaPipelineFeatureToggleMonitor>(MockBehavior.Strict);
            var archiveQueue = new Mock<IImageFrameArchiveIngestionQueue>(MockBehavior.Strict);
            archiveQueue.Setup(q => q.TryEnqueue(It.IsAny<ImageFrameArchiveIngestionRequest>())).Returns(true);
            var imageHistoryOptions = new Mock<IOptionsMonitor<ImageHistoryOptions>>();
            imageHistoryOptions.SetupGet(o => o.CurrentValue).Returns(new ImageHistoryOptions());

            var publisher = new FrameExportPublisher(
                dispatcher,
                encoder,
                NullLogger<FrameExportPublisher>.Instance,
                featureOptions.Object,
                featureMonitor.Object,
                archiveQueue.Object,
                imageHistoryOptions.Object);

            publisher.PublishRawFrame(
                frameNumber: 1,
                capture,
                rig,
                captureMilliseconds: 5.0,
                stageTimestampUtc: stageTimestampUtc);

            publisher.PublishProcessedFrame(
                frameNumber: 1,
                stackResult,
                processedFrame,
                rig,
                queueLatencyMilliseconds: 5.0,
                processingMilliseconds: 0.0,
                stageTimestampUtc: stageTimestampUtc);

            var yearSegment = stageTimestampUtc.ToString("yyyy", CultureInfo.InvariantCulture);
            var monthSegment = stageTimestampUtc.ToString("MM", CultureInfo.InvariantCulture);
            var daySegment = stageTimestampUtc.ToString("dd", CultureInfo.InvariantCulture);

            var rawArchiveDir = Path.Combine(rootPath, "fixtures", "raw", "archive", yearSegment, monthSegment, daySegment);
            var rawDeliveryDir = Path.Combine(rootPath, "fixtures", "raw", "delivery", yearSegment, monthSegment, daySegment);
            var processedArchiveDir = Path.Combine(rootPath, "fixtures", "processed", "archive", yearSegment, monthSegment, daySegment);
            var processedDeliveryDir = Path.Combine(rootPath, "fixtures", "processed", "delivery", yearSegment, monthSegment, daySegment);

            var rawArchivePayloads = await WaitForFilesAsync(rawArchiveDir, "*.skimg", 1, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            var rawDeliveryPayloads = await WaitForFilesAsync(rawDeliveryDir, "*.skimg", 1, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            var processedArchivePayloads = await WaitForFilesAsync(processedArchiveDir, "*.png", 1, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            var processedDeliveryPayloads = await WaitForFilesAsync(processedDeliveryDir, "*.png", 1, TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            var expectedBaseName = FormattableString.Invariant($"{stageTimestampUtc:HHmmssfff}-{frameId:N}");

            ValidatePayload(rawArchivePayloads[0], expectedRawPayload, "062DBE6DC896E8CD19F3B6C3EAD6B5FA12D86860AED68ED2D8E412A85B8C6C89", expectedBaseName);
            ValidatePayload(rawDeliveryPayloads[0], expectedRawPayload, "062DBE6DC896E8CD19F3B6C3EAD6B5FA12D86860AED68ED2D8E412A85B8C6C89", expectedBaseName);
            ValidatePayload(processedArchivePayloads[0], expectedDeliveryPayload, "C22A5C3A47ACA7F5A8E2A146E34ED5DDF5D3F68AAB129054A6758C36C225D196", expectedBaseName);
            ValidatePayload(processedDeliveryPayloads[0], expectedDeliveryPayload, "C22A5C3A47ACA7F5A8E2A146E34ED5DDF5D3F68AAB129054A6758C36C225D196", expectedBaseName);

            ValidateManifest(rawArchivePayloads[0], SkiaRawFrameHelper.RawContentType, SkiaRawFrameHelper.RawFileExtension, stageTimestampUtc);
            ValidateManifest(rawDeliveryPayloads[0], SkiaRawFrameHelper.RawContentType, SkiaRawFrameHelper.RawFileExtension, stageTimestampUtc);
            ValidateManifest(processedArchivePayloads[0], expectedDelivery.ContentType, expectedDelivery.FileExtension, stageTimestampUtc);
            ValidateManifest(processedDeliveryPayloads[0], expectedDelivery.ContentType, expectedDelivery.FileExtension, stageTimestampUtc);

            var manifests = Directory.GetFiles(rootPath, "*.json", SearchOption.AllDirectories);
            Assert.HasCount(4, manifests, "Each payload role should emit a metadata manifest.");
        }
        finally
        {
            if (dispatcher is not null)
            {
                await dispatcher.StopAsync(CancellationToken.None).ConfigureAwait(false);
                dispatcher.Dispose();
            }

            try
            {
                if (Directory.Exists(rootPath))
                {
                    Directory.Delete(rootPath, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup; tests should not fail due to transient file locks.
            }
        }
    }

    private static void ValidatePayload(string path, byte[] expectedBytes, string expectedHash, string expectedBaseName)
    {
        Assert.AreEqual(expectedBaseName, Path.GetFileNameWithoutExtension(path), "Payload file name should follow timestamp-frameId pattern.");

        var actualBytes = File.ReadAllBytes(path);
        CollectionAssert.AreEqual(expectedBytes, actualBytes, "Payload bytes should match golden fixture content.");

        var hash = Convert.ToHexString(SHA256.HashData(actualBytes));
        Assert.AreEqual(expectedHash, hash, "Payload hash should remain stable for golden fixture.");
    }

    private static void ValidateManifest(string payloadPath, string? expectedContentType, string? expectedExtension, DateTimeOffset expectedStageTimestampUtc)
    {
        var manifestPath = Path.ChangeExtension(payloadPath, ".json");
        Assert.IsTrue(File.Exists(manifestPath), "Manifest should exist alongside payload.");

        using var document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        var root = document.RootElement;

        Assert.AreEqual(expectedContentType, root.GetProperty("payloadContentType").GetString(), "Manifest should capture payload content type.");
        Assert.AreEqual(expectedExtension, root.GetProperty("payloadExtension").GetString(), "Manifest should capture payload extension.");

        var stageTimestampValue = root.GetProperty("stageTimestampUtc").GetDateTimeOffset();
        Assert.AreEqual(expectedStageTimestampUtc, stageTimestampValue, "Manifest should retain stage timestamp.");
    }

    private static async Task<string[]> WaitForFilesAsync(string directory, string searchPattern, int expectedCount, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        string[] files = Array.Empty<string>();

        while (DateTime.UtcNow <= deadline)
        {
            if (Directory.Exists(directory))
            {
                files = Directory.GetFiles(directory, searchPattern, SearchOption.TopDirectoryOnly);
                if (files.Length >= expectedCount)
                {
                    return files;
                }
            }

            await Task.Delay(50).ConfigureAwait(false);
        }

        Assert.Fail(FormattableString.Invariant($"Expected {expectedCount} file(s) matching '{searchPattern}' in '{directory}', but found {files.Length}."));
        return files;
    }
}
