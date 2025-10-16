using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Exports;
using HVO.SkyMonitorV5.RPi.Exports.Sinks;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Services.RemoteDispatch;
using HVO.SkyMonitorV5.RPi.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Minio;
using Minio.DataModel.Args;
using Minio.DataModel.Response;
using Moq;

namespace HVO.SkyMonitorV5.RPi.Tests.Exports;

[TestClass]
public sealed class S3FrameExportSinkTests
{
    [TestMethod]
    public async Task ExportAsync_UploadsPayloadAndManifest()
    {
        var options = new FrameExportOptions();
        options.Raw.Enabled = true;
        options.Raw.S3.Add(new S3FrameExportSinkOptions
        {
            Enabled = true,
            Bucket = "hvo-test",
            Endpoint = "play.min.io",
            AccessKey = "access",
            SecretKey = "secret",
            Prefix = "frames",
            UseSsl = true,
            EmitJsonManifest = true,
            EmitMetadataHeaders = true
        });
        options.Normalize();

        using var optionsMonitor = new TestOptionsMonitor<FrameExportOptions>(options);

        var clientProvider = new Mock<IMinioClientProvider>(MockBehavior.Strict);
    var minioClient = new Mock<IMinioClient>(MockBehavior.Strict);
    var capturedCalls = new List<PutObjectArgs>();
    var capturedBodies = new List<byte[]>();
    var resilienceProvider = new TestResiliencePolicyProvider();

        clientProvider
            .Setup(provider => provider.GetClient("play.min.io", "access", "secret", true))
            .Returns(minioClient.Object);

        minioClient
            .Setup(client => client.BucketExistsAsync(It.IsAny<BucketExistsArgs>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        minioClient
            .Setup(client => client.PutObjectAsync(It.IsAny<PutObjectArgs>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectArgs, CancellationToken>((args, _) =>
            {
                capturedCalls.Add(args);

                var stream = GetPropertyValue<System.IO.Stream>(args, "ObjectStreamData");
                if (stream is not null)
                {
                    if (stream.CanSeek)
                    {
                        stream.Position = 0;
                    }

                    using var buffer = new System.IO.MemoryStream();
                    stream.CopyTo(buffer);
                    capturedBodies.Add(buffer.ToArray());

                    if (stream.CanSeek)
                    {
                        stream.Position = 0;
                    }
                }
                else
                {
                    var requestBody = GetPropertyValue<ReadOnlyMemory<byte>>(args, "RequestBody");
                    capturedBodies.Add(requestBody.IsEmpty ? Array.Empty<byte>() : requestBody.ToArray());
                }
            })
            .Returns(Task.FromResult(default(PutObjectResponse)!));

        var sink = new S3FrameExportSink(
            FrameExportStage.Raw,
            optionsMonitor,
            clientProvider.Object,
            resilienceProvider,
            NullLogger<S3FrameExportSink>.Instance);

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
            new ExposureSettings(800, 150, false, false),
            RigName: "Rig",
            CameraName: "Camera",
            LensName: "Lens",
            LatitudeDeg: 35.0,
            LongitudeDeg: -114.0,
            FlipHorizontal: false,
            HorizonPadding: null,
            ApplyRefraction: false,
            FramesStacked: 2,
            IntegrationMilliseconds: 1600,
            AppliedFilters: new List<string> { "FilterA", "FilterB" },
            QueueLatencyMilliseconds: 5.5,
            ProcessingMilliseconds: 10.1,
            FullPipelineMilliseconds: 1615.6,
            RawImageDescriptor: descriptor,
            PayloadContentType: "application/vnd.hvo.skia.raw",
            PayloadExtension: "skimg");

        var payload = new ReadOnlyMemory<byte>(new byte[] { 5, 4, 3, 2, 1 });
        var envelope = new FrameExportEnvelope(
            frameId,
            FrameExportStage.Raw,
            metadata,
            payload,
            "application/vnd.hvo.skia.raw",
            "skimg");

        var result = await sink.ExportAsync(envelope, CancellationToken.None);

        Assert.IsTrue(result.IsSuccessful, "Expected upload to succeed.");
        Assert.IsTrue(result.Value, "Expected sink to report persistence.");
        Assert.AreEqual(2, capturedCalls.Count, "Expected payload and manifest uploads.");

        var payloadCall = capturedCalls[0];
        var objectName = (string?)payloadCall.GetType().GetProperty("ObjectName", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(payloadCall);
        Assert.IsNotNull(objectName, "Expected object name to be populated.");
        Assert.IsTrue(objectName.EndsWith(".skimg", StringComparison.Ordinal), "Expected object extension to track payload metadata.");
        StringAssert.Contains(objectName, "/archive/", "Prefix should route archive payloads to archive scope.");

        var contentType = (string?)payloadCall.GetType().GetProperty("ContentType", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(payloadCall);
        Assert.AreEqual("application/vnd.hvo.skia.raw", contentType, "Expected content type to be sourced from payload metadata.");

        CollectionAssert.AreEqual(payload.ToArray(), capturedBodies[0], "Payload bytes should match the provided data.");

        var headers = payloadCall.GetType().GetProperty("Headers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(payloadCall) as IDictionary<string, string>;
        Assert.IsNotNull(headers, "Expected headers to be populated when metadata emission is enabled.");
        var headerContentType = GetHeaderValue(headers, "payload-content-type");
        Assert.AreEqual("application/vnd.hvo.skia.raw", headerContentType, "Expected payload-content-type header to reflect metadata.");

        var headerExtension = GetHeaderValue(headers, "payload-extension");
        Assert.AreEqual("skimg", headerExtension, "Expected payload-extension header to reflect metadata.");
        var headerRole = GetHeaderValue(headers, "payload-role");
        Assert.AreEqual("archive", headerRole, "Expected payload-role header to reflect archive scope.");

        var manifestCall = capturedCalls[1];
        var manifestContentType = (string?)manifestCall.GetType().GetProperty("ContentType", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(manifestCall);
        Assert.AreEqual("application/json", manifestContentType, "Manifest upload should advertise JSON content type.");

        var manifestJson = Encoding.UTF8.GetString(capturedBodies[1]);

        var manifestPayload = JsonSerializer.Serialize(metadata, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.AreEqual(manifestPayload, manifestJson, "Manifest payload should mirror serialized metadata.");

        clientProvider.VerifyAll();
        minioClient.VerifyAll();
    }

    [TestMethod]
    public async Task ExportAsync_WithArchiveAndDeliveryScope_DuplicatesUploadsPerRole()
    {
        var options = new FrameExportOptions();
        options.Raw.Enabled = true;
        options.Raw.PayloadScope = FrameExportPayloadScope.ArchiveAndDelivery;
        options.Raw.S3.Add(new S3FrameExportSinkOptions
        {
            Enabled = true,
            Bucket = "hvo-test",
            Endpoint = "play.min.io",
            AccessKey = "access",
            SecretKey = "secret",
            Prefix = "frames",
            UseSsl = true,
            EmitJsonManifest = true,
            EmitMetadataHeaders = true
        });
        options.Normalize();

        using var optionsMonitor = new TestOptionsMonitor<FrameExportOptions>(options);

        var clientProvider = new Mock<IMinioClientProvider>(MockBehavior.Strict);
        var minioClient = new Mock<IMinioClient>(MockBehavior.Strict);
        var capturedCalls = new List<(PutObjectArgs Args, byte[] Body)>();
        var resilienceProvider = new TestResiliencePolicyProvider();

        clientProvider
            .Setup(provider => provider.GetClient("play.min.io", "access", "secret", true))
            .Returns(minioClient.Object);

        minioClient
            .Setup(client => client.BucketExistsAsync(It.IsAny<BucketExistsArgs>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        minioClient
            .Setup(client => client.PutObjectAsync(It.IsAny<PutObjectArgs>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectArgs, CancellationToken>((args, _) =>
            {
                var stream = GetPropertyValue<System.IO.Stream>(args, "ObjectStreamData");
                byte[] payload;
                if (stream is not null)
                {
                    if (stream.CanSeek)
                    {
                        stream.Position = 0;
                    }

                    using var buffer = new System.IO.MemoryStream();
                    stream.CopyTo(buffer);
                    payload = buffer.ToArray();

                    if (stream.CanSeek)
                    {
                        stream.Position = 0;
                    }
                }
                else
                {
                    var requestBody = GetPropertyValue<ReadOnlyMemory<byte>>(args, "RequestBody");
                    payload = requestBody.IsEmpty ? Array.Empty<byte>() : requestBody.ToArray();
                }

                capturedCalls.Add((args, payload));
            })
            .Returns(Task.FromResult(default(PutObjectResponse)!));

        var sink = new S3FrameExportSink(
            FrameExportStage.Raw,
            optionsMonitor,
            clientProvider.Object,
            resilienceProvider,
            NullLogger<S3FrameExportSink>.Instance);

        var frameId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2025, 10, 13, 9, 30, 0, TimeSpan.Zero);
        var descriptor = new FrameExportImageDescriptor(8, 8, 64, 8, "RgbaF16", "Premul", true, false, true, "Linear");

        var metadata = new FrameExportMetadata(
            frameId,
            timestamp,
            timestamp,
            new ExposureSettings(500, 140, false, false),
            "Rig",
            "Camera",
            "Lens",
            35.0,
            -114.0,
            false,
            null,
            false,
            1,
            500,
            AppliedFilters: Array.Empty<string>(),
            QueueLatencyMilliseconds: null,
            ProcessingMilliseconds: null,
            FullPipelineMilliseconds: null,
            RawImageDescriptor: descriptor,
            PayloadContentType: "application/vnd.hvo.skia.raw",
            PayloadExtension: "skimg");

        var payload = new ReadOnlyMemory<byte>(new byte[] { 9, 8, 7, 6 });
        var envelope = new FrameExportEnvelope(
            frameId,
            FrameExportStage.Raw,
            metadata,
            payload,
            "application/vnd.hvo.skia.raw",
            "skimg");

        var result = await sink.ExportAsync(envelope, CancellationToken.None);

        Assert.IsTrue(result.IsSuccessful && result.Value, "Expected export to succeed when dual scope configured.");
        Assert.AreEqual(4, capturedCalls.Count, "Dual scope should upload two payloads and two manifests.");

        var archivePayload = capturedCalls[0];
        var archiveManifest = capturedCalls[1];
        var deliveryPayload = capturedCalls[2];
        var deliveryManifest = capturedCalls[3];

        StringAssert.Contains(GetObjectName(archivePayload.Args), "/archive/", "First payload should target archive prefix.");
        StringAssert.Contains(GetObjectName(deliveryPayload.Args), "/delivery/", "Second payload should target delivery prefix.");

        CollectionAssert.AreEqual(payload.ToArray(), archivePayload.Body, "Archive payload contents should match source payload.");
        CollectionAssert.AreEqual(payload.ToArray(), deliveryPayload.Body, "Delivery payload contents should match source payload.");

        Assert.AreEqual("application/json", GetContentType(archiveManifest.Args), "Archive manifest should be JSON.");
        Assert.AreEqual("application/json", GetContentType(deliveryManifest.Args), "Delivery manifest should be JSON.");

        clientProvider.VerifyAll();
        minioClient.VerifyAll();
    }

    private static string GetObjectName(PutObjectArgs args)
        => (string?)args.GetType().GetProperty("ObjectName", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(args)
           ?? string.Empty;

    private static string? GetContentType(PutObjectArgs args)
        => (string?)args.GetType().GetProperty("ContentType", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(args);

    private static string? GetHeaderValue(IDictionary<string, string> headers, string suffix)
    {
        foreach (var (key, value) in headers)
        {
            if (key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }

    private static T GetPropertyValue<T>(object target, string propertyName)
    {
        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        var property = target.GetType().GetProperty(propertyName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (property is null)
        {
            return default!;
        }

        var value = property.GetValue(target);
        return value is null ? default! : (T)value;
    }
}
