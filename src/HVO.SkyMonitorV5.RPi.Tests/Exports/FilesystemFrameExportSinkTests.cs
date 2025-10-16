using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Exports;
using HVO.SkyMonitorV5.RPi.Exports.Sinks;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HVO.SkyMonitorV5.RPi.Tests.Exports;

[TestClass]
public sealed class FilesystemFrameExportSinkTests
{
    [TestMethod]
    public async Task ExportAsync_WritesPayloadAndManifest()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), FormattableString.Invariant($"hvo-fsexport-{Guid.NewGuid():N}"));
        Directory.CreateDirectory(rootPath);

        try
        {
            var options = new FrameExportOptions();
            options.Raw.Enabled = true;
            options.Raw.Filesystem.Add(new FilesystemFrameExportSinkOptions
            {
                Enabled = true,
                RootPath = rootPath,
                IncludeMetadataManifest = true
            });
            options.Normalize();

            using var optionsMonitor = new TestOptionsMonitor<FrameExportOptions>(options);
            var sink = new FilesystemFrameExportSink(
                FrameExportStage.Raw,
                optionsMonitor,
                NullLogger<FilesystemFrameExportSink>.Instance);

            var frameId = Guid.NewGuid();
            var timestamp = new DateTimeOffset(2025, 10, 13, 12, 34, 56, TimeSpan.Zero);
            var descriptor = new FrameExportImageDescriptor(
                Width: 1920,
                Height: 1080,
                RowBytes: 1920 * 8,
                BytesPerPixel: 8,
                ColorType: "RgbaF16",
                AlphaType: "Premul",
                GammaIsLinear: true,
                IsSrgb: false,
                HasNumericalTransferFunction: true,
                ColorSpaceDescription: "Linear SRGB");

            var metadata = new FrameExportMetadata(
                frameId,
                timestamp,
                timestamp,
                new ExposureSettings(1000, 200, false, false),
                RigName: "Rig One",
                CameraName: "Camera X",
                LensName: "Lens Y",
                LatitudeDeg: 35.0,
                LongitudeDeg: -114.0,
                FlipHorizontal: false,
                HorizonPadding: null,
                ApplyRefraction: false,
                FramesStacked: 1,
                IntegrationMilliseconds: 1000,
                AppliedFilters: new List<string> { "MockFilter" },
                QueueLatencyMilliseconds: 12.3,
                ProcessingMilliseconds: 45.6,
        FullPipelineMilliseconds: 1057.9,
    RawImageDescriptor: descriptor,
    PayloadContentType: "application/vnd.hvo.skia.raw",
    PayloadExtension: "skimg");

            var payload = new ReadOnlyMemory<byte>(new byte[] { 1, 2, 3, 4 });
            var envelope = new FrameExportEnvelope(
                frameId,
                FrameExportStage.Raw,
                metadata,
    payload,
    "application/vnd.hvo.skia.raw",
    "skimg");

            var result = await sink.ExportAsync(envelope, CancellationToken.None);

            Assert.IsTrue(result.IsSuccessful, "Expected successful export result.");
            Assert.IsTrue(result.Value, "Expected sink to report payload persisted.");

            var stageDirectory = Path.Combine(rootPath, "archive", "2025", "10", "13");
            Assert.IsTrue(Directory.Exists(stageDirectory), "Expected stage directory to be created.");

            var imageFiles = Directory.GetFiles(stageDirectory, "*.skimg");
            Assert.AreEqual(1, imageFiles.Length, "Expected exactly one image to be written.");
            CollectionAssert.AreEqual(payload.ToArray(), await File.ReadAllBytesAsync(imageFiles[0]));

            var manifestFiles = Directory.GetFiles(stageDirectory, "*.json");
            Assert.AreEqual(1, manifestFiles.Length, "Expected JSON manifest to be written.");

            var manifestJson = await File.ReadAllTextAsync(manifestFiles[0]);
            using var document = JsonDocument.Parse(manifestJson);
            Assert.AreEqual(frameId.ToString("D"), document.RootElement.GetProperty("frameId").GetString());
            Assert.AreEqual(1057.9, document.RootElement.GetProperty("fullPipelineMilliseconds").GetDouble(), 0.0001, "Manifest should include full pipeline duration.");
            Assert.AreEqual("application/vnd.hvo.skia.raw", document.RootElement.GetProperty("payloadContentType").GetString(), "Manifest should note payload content type.");
            Assert.AreEqual("skimg", document.RootElement.GetProperty("payloadExtension").GetString(), "Manifest should note payload extension.");
            var rawDescriptor = document.RootElement.GetProperty("rawImageDescriptor");
            Assert.AreEqual(1920, rawDescriptor.GetProperty("width").GetInt32(), "Raw descriptor width should persist.");
            Assert.AreEqual(1080, rawDescriptor.GetProperty("height").GetInt32(), "Raw descriptor height should persist.");
            Assert.AreEqual("RgbaF16", rawDescriptor.GetProperty("colorType").GetString(), "Raw descriptor color type should persist.");
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task ExportAsync_WithArchiveAndDeliveryScope_WritesToBothTargets()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), FormattableString.Invariant($"hvo-fsexport-dual-{Guid.NewGuid():N}"));
        Directory.CreateDirectory(rootPath);

        try
        {
            var options = new FrameExportOptions();
            options.Raw.Enabled = true;
            options.Raw.PayloadScope = FrameExportPayloadScope.ArchiveAndDelivery;
            options.Raw.Filesystem.Add(new FilesystemFrameExportSinkOptions
            {
                Enabled = true,
                RootPath = rootPath,
                IncludeMetadataManifest = false
            });
            options.Normalize();

            using var optionsMonitor = new TestOptionsMonitor<FrameExportOptions>(options);
            var sink = new FilesystemFrameExportSink(
                FrameExportStage.Raw,
                optionsMonitor,
                NullLogger<FilesystemFrameExportSink>.Instance);

            var frameId = Guid.NewGuid();
            var timestamp = new DateTimeOffset(2025, 10, 13, 8, 15, 42, TimeSpan.Zero);
            var descriptor = new FrameExportImageDescriptor(8, 8, 64, 8, "RgbaF16", "Premul", true, false, true, "Linear");

            var metadata = new FrameExportMetadata(
                frameId,
                timestamp,
                timestamp,
                new ExposureSettings(250, 100, false, false),
                "Rig",
                "Camera",
                "Lens",
                35.0,
                -114.0,
                false,
                null,
                false,
                1,
                250,
                AppliedFilters: Array.Empty<string>(),
                QueueLatencyMilliseconds: null,
                ProcessingMilliseconds: null,
                FullPipelineMilliseconds: null,
                RawImageDescriptor: descriptor,
                PayloadContentType: "application/vnd.hvo.skia.raw",
                PayloadExtension: "skimg");

            var payload = new ReadOnlyMemory<byte>(new byte[] { 5, 6, 7, 8 });
            var envelope = new FrameExportEnvelope(
                frameId,
                FrameExportStage.Raw,
                metadata,
                payload,
                "application/vnd.hvo.skia.raw",
                "skimg");

            var result = await sink.ExportAsync(envelope, CancellationToken.None);

            Assert.IsTrue(result.IsSuccessful && result.Value, "Expected dual-scope export to succeed.");

            var archivePath = Path.Combine(rootPath, "archive", "2025", "10", "13");
            var deliveryPath = Path.Combine(rootPath, "delivery", "2025", "10", "13");

            Assert.IsTrue(Directory.Exists(archivePath), "Archive directory should exist.");
            Assert.IsTrue(Directory.Exists(deliveryPath), "Delivery directory should exist.");

            var archiveFiles = Directory.GetFiles(archivePath, "*.skimg");
            var deliveryFiles = Directory.GetFiles(deliveryPath, "*.skimg");

            Assert.AreEqual(1, archiveFiles.Length, "Archive scope should produce one file.");
            Assert.AreEqual(1, deliveryFiles.Length, "Delivery scope should produce one file.");

            CollectionAssert.AreEqual(payload.ToArray(), await File.ReadAllBytesAsync(archiveFiles[0]));
            CollectionAssert.AreEqual(payload.ToArray(), await File.ReadAllBytesAsync(deliveryFiles[0]));
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}
