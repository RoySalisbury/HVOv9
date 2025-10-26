using System;
using System.Collections.Generic;
using System.IO;
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
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using Moq;

namespace HVO.SkyMonitorV5.RPi.Tests.Exports;

[TestClass]
public sealed class S3FrameExportSinkIntegrationTests
{
    public TestContext? TestContext { get; set; }

    [TestMethod]
    [TestCategory("MinioDev")]
    public async Task ExportAsync_CreatesBucketWhenMissing_OnMinioDev()
    {
        var endpoint = Environment.GetEnvironmentVariable("HVO_MINIO_DEV_ENDPOINT");
        var accessKey = Environment.GetEnvironmentVariable("HVO_MINIO_DEV_ACCESS_KEY");
        var secretKey = Environment.GetEnvironmentVariable("HVO_MINIO_DEV_SECRET_KEY");
        var useSsl = string.Equals(Environment.GetEnvironmentVariable("HVO_MINIO_DEV_USE_SSL"), "true", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretKey))
        {
            Assert.Inconclusive("Set HVO_MINIO_DEV_ENDPOINT/ACCESS_KEY/SECRET_KEY to run MinIO integration tests.");
        }

        var bucket = FormattableString.Invariant($"hvo-integration-{Guid.NewGuid():N}");
        var prefix = "integration-tests";

        var options = new FrameExportOptions();
        options.Raw.Enabled = true;
        options.Raw.S3.Add(new S3FrameExportSinkOptions
        {
            Enabled = true,
            Bucket = bucket,
            Prefix = prefix,
            Endpoint = endpoint,
            AccessKey = accessKey,
            SecretKey = secretKey,
            UseSsl = useSsl,
            EmitJsonManifest = true,
            EmitMetadataHeaders = true
        });
        options.Normalize();

        using var provider = new MinioClientProvider(NullLogger<MinioClientProvider>.Instance);
        using var optionsMonitor = new TestOptionsMonitor<FrameExportOptions>(options);
        var resilienceProvider = new TestResiliencePolicyProvider();
        
        var mockHealthCheck = new Mock<HealthCheckService>();
        mockHealthCheck.Setup(h => h.CheckHealthAsync(It.IsAny<Func<HealthCheckRegistration, bool>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HealthReport(new Dictionary<string, HealthReportEntry>(), HealthStatus.Healthy, TimeSpan.Zero));
        
        var sink = new S3FrameExportSink(
            FrameExportStage.Raw,
            optionsMonitor,
            provider,
            resilienceProvider,
            mockHealthCheck.Object,
            NullLogger<S3FrameExportSink>.Instance);

        var frameId = Guid.CreateVersion7();
        var timestampUtc = DateTimeOffset.UtcNow;
        var descriptor = new FrameExportImageDescriptor(
            Width: 640,
            Height: 480,
            RowBytes: 640 * 8,
            BytesPerPixel: 8,
            ColorType: "RgbaF16",
            AlphaType: "Premul",
            GammaIsLinear: true,
            IsSrgb: false,
            HasNumericalTransferFunction: true,
            ColorSpaceDescription: "Linear SRGB");

        var metadata = new FrameExportMetadata(
            frameId,
            timestampUtc,
            timestampUtc,
            new ExposureSettings(750, 180, false, false),
            "IntegrationRig",
            "IntegrationCamera",
            "IntegrationLens",
            35.3125,
            -114.1234,
            false,
            null,
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            RawImageDescriptor: descriptor,
            PayloadContentType: "application/vnd.hvo.skia.raw",
            PayloadExtension: "skimg");

        var payload = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var envelope = new FrameExportEnvelope(
            frameId,
            FrameExportStage.Raw,
            metadata,
            payload.AsMemory(),
            "application/vnd.hvo.skia.raw",
            "skimg");

    var prefixPath = options.Raw.S3[0].BuildObjectPrefix(FrameExportPayloadRole.Archive, timestampUtc);
        var baseFileName = FormattableString.Invariant($"{timestampUtc:HHmmssfff}-{frameId:N}");
            var payloadKey = FormattableString.Invariant($"{prefixPath}/{baseFileName}.skimg");
            var manifestKey = FormattableString.Invariant($"{prefixPath}/{baseFileName}.json");

    var client = provider.GetClient(endpoint!, accessKey!, secretKey!, useSsl);

        try
        {
            var result = await sink.ExportAsync(envelope, CancellationToken.None).ConfigureAwait(false);

            Assert.IsTrue(result.IsSuccessful, "Expected export to succeed.");
            Assert.IsTrue(result.Value, "Expected sink to report persistence.");

            var exists = await client.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucket), CancellationToken.None).ConfigureAwait(false);
            Assert.IsTrue(exists, "Expected bucket to exist after export.");

            var payloadStat = await client.StatObjectAsync(new StatObjectArgs().WithBucket(bucket).WithObject(payloadKey), CancellationToken.None).ConfigureAwait(false);
            var manifestStat = await client.StatObjectAsync(new StatObjectArgs().WithBucket(bucket).WithObject(manifestKey), CancellationToken.None).ConfigureAwait(false);

            var contentTypeProperty = payloadStat.GetType().GetProperty("ContentType", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            var contentTypeValue = (string?)contentTypeProperty?.GetValue(payloadStat);
            Assert.AreEqual("application/vnd.hvo.skia.raw", contentTypeValue, "Payload object should advertise metadata-derived content type.");

            var metadataDictionary = GetPropertyValue<IDictionary<string, string>>(payloadStat, "MetaData");
            Assert.IsNotNull(metadataDictionary, "Expected metadata dictionary on payload stat response.");
            var headerContentType = GetHeaderValue(metadataDictionary, "payload-content-type");
            Assert.AreEqual("application/vnd.hvo.skia.raw", headerContentType, "Expected payload metadata header to propagate content type.");
            var headerExtension = GetHeaderValue(metadataDictionary, "payload-extension");
            Assert.AreEqual("skimg", headerExtension, "Expected payload metadata header to propagate file extension.");
            var headerRole = GetHeaderValue(metadataDictionary, "payload-role");
            Assert.AreEqual("archive", headerRole, "Expected payload metadata header to propagate role scope.");

            var manifestBuffer = new MemoryStream();
            var getArgs = new GetObjectArgs()
                .WithBucket(bucket)
                .WithObject(manifestKey)
                .WithCallbackStream(async (stream, token) =>
                {
                    if (stream.CanSeek)
                    {
                        stream.Position = 0;
                    }

                    await stream.CopyToAsync(manifestBuffer, token).ConfigureAwait(false);
                    manifestBuffer.Position = 0;
                });

            await client.GetObjectAsync(getArgs, CancellationToken.None).ConfigureAwait(false);

            var manifestJson = Encoding.UTF8.GetString(manifestBuffer.ToArray());
            var expectedJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.AreEqual(expectedJson, manifestJson, "Manifest JSON retrieved from MinIO should match serialized metadata.");
        }
        finally
        {
            await CleanupObjectAsync(client, bucket, payloadKey).ConfigureAwait(false);
            await CleanupObjectAsync(client, bucket, manifestKey).ConfigureAwait(false);
            await CleanupBucketAsync(client, bucket).ConfigureAwait(false);
        }
    }

    [TestMethod]
    [TestCategory("MinioDev")]
    public async Task ExportAsync_WithArchiveAndDeliveryScope_PersistsDualPayloads_OnMinioDev()
    {
        var endpoint = Environment.GetEnvironmentVariable("HVO_MINIO_DEV_ENDPOINT");
        var accessKey = Environment.GetEnvironmentVariable("HVO_MINIO_DEV_ACCESS_KEY");
        var secretKey = Environment.GetEnvironmentVariable("HVO_MINIO_DEV_SECRET_KEY");
        var useSsl = string.Equals(Environment.GetEnvironmentVariable("HVO_MINIO_DEV_USE_SSL"), "true", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretKey))
        {
            Assert.Inconclusive("Set HVO_MINIO_DEV_ENDPOINT/ACCESS_KEY/SECRET_KEY to run MinIO integration tests.");
        }

        var bucket = FormattableString.Invariant($"hvo-fixtures-{Guid.NewGuid():N}");
        var prefix = "fixtures/raw";

        var options = new FrameExportOptions();
        options.Raw.Enabled = true;
        options.Raw.PayloadScope = FrameExportPayloadScope.ArchiveAndDelivery;
        options.Raw.S3.Add(new S3FrameExportSinkOptions
        {
            Enabled = true,
            Bucket = bucket,
            Prefix = prefix,
            Endpoint = endpoint,
            AccessKey = accessKey,
            SecretKey = secretKey,
            UseSsl = useSsl,
            EmitJsonManifest = true,
            EmitMetadataHeaders = true
        });
        options.Normalize();

        using var provider = new MinioClientProvider(NullLogger<MinioClientProvider>.Instance);
        using var optionsMonitor = new TestOptionsMonitor<FrameExportOptions>(options);
        var resilienceProvider = new TestResiliencePolicyProvider();
        
        var mockHealthCheck = new Mock<HealthCheckService>();
        mockHealthCheck.Setup(h => h.CheckHealthAsync(It.IsAny<Func<HealthCheckRegistration, bool>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HealthReport(new Dictionary<string, HealthReportEntry>(), HealthStatus.Healthy, TimeSpan.Zero));
        
        var sink = new S3FrameExportSink(
            FrameExportStage.Raw,
            optionsMonitor,
            provider,
            resilienceProvider,
            mockHealthCheck.Object,
            NullLogger<S3FrameExportSink>.Instance);

        var frameId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeffffffff");
        var timestampUtc = new DateTimeOffset(2025, 10, 16, 0, 1, 0, TimeSpan.Zero);
        var descriptor = new FrameExportImageDescriptor(
            Width: 4,
            Height: 4,
            RowBytes: 16,
            BytesPerPixel: 4,
            ColorType: "Rgba8888",
            AlphaType: "Premul",
            GammaIsLinear: true,
            IsSrgb: false,
            HasNumericalTransferFunction: true,
            ColorSpaceDescription: "Linear");

        var metadata = new FrameExportMetadata(
            frameId,
            timestampUtc,
            timestampUtc,
            new ExposureSettings(1000, 200, false, false),
            "FixtureRig",
            "FixtureCamera",
            "FixtureLens",
            35.0,
            -114.0,
            false,
            null,
            false,
            1,
            1000,
            AppliedFilters: Array.Empty<string>(),
            QueueLatencyMilliseconds: 5.0,
            ProcessingMilliseconds: null,
            FullPipelineMilliseconds: null,
            RawImageDescriptor: descriptor,
            PayloadContentType: "application/vnd.hvo.skia.raw",
            PayloadExtension: "skimg");

        var payload = new byte[] { 0x0A, 0x0B, 0x0C, 0x0D };
        var envelope = new FrameExportEnvelope(
            frameId,
            FrameExportStage.Raw,
            metadata,
            payload.AsMemory(),
            "application/vnd.hvo.skia.raw",
            "skimg");

        var archivePrefix = options.Raw.S3[0].BuildObjectPrefix(FrameExportPayloadRole.Archive, timestampUtc);
        var deliveryPrefix = options.Raw.S3[0].BuildObjectPrefix(FrameExportPayloadRole.Delivery, timestampUtc);
        var baseFileName = FormattableString.Invariant($"{timestampUtc:HHmmssfff}-{frameId:N}");

        var archivePayloadKey = FormattableString.Invariant($"{archivePrefix}/{baseFileName}.skimg");
        var archiveManifestKey = FormattableString.Invariant($"{archivePrefix}/{baseFileName}.json");
        var deliveryPayloadKey = FormattableString.Invariant($"{deliveryPrefix}/{baseFileName}.skimg");
        var deliveryManifestKey = FormattableString.Invariant($"{deliveryPrefix}/{baseFileName}.json");

        var client = provider.GetClient(endpoint!, accessKey!, secretKey!, useSsl);

        try
        {
            var result = await sink.ExportAsync(envelope, CancellationToken.None).ConfigureAwait(false);

            Assert.IsTrue(result.IsSuccessful && result.Value, "Expected S3 export to succeed for dual scope.");

            var archiveStat = await client.StatObjectAsync(new StatObjectArgs().WithBucket(bucket).WithObject(archivePayloadKey), CancellationToken.None).ConfigureAwait(false);
            var deliveryStat = await client.StatObjectAsync(new StatObjectArgs().WithBucket(bucket).WithObject(deliveryPayloadKey), CancellationToken.None).ConfigureAwait(false);

            var archiveHeaders = GetPropertyValue<IDictionary<string, string>>(archiveStat, "MetaData");
            var deliveryHeaders = GetPropertyValue<IDictionary<string, string>>(deliveryStat, "MetaData");

            Assert.AreEqual("archive", GetHeaderValue(archiveHeaders, "payload-role"), "Archive payload header should indicate archive role.");
            Assert.AreEqual("delivery", GetHeaderValue(deliveryHeaders, "payload-role"), "Delivery payload header should indicate delivery role.");
            Assert.AreEqual("application/vnd.hvo.skia.raw", GetHeaderValue(archiveHeaders, "payload-content-type"));
            Assert.AreEqual("application/vnd.hvo.skia.raw", GetHeaderValue(deliveryHeaders, "payload-content-type"));
            Assert.AreEqual("skimg", GetHeaderValue(archiveHeaders, "payload-extension"));
            Assert.AreEqual("skimg", GetHeaderValue(deliveryHeaders, "payload-extension"));

            var archivePayloadBytes = await GetObjectBytesAsync(client, bucket, archivePayloadKey).ConfigureAwait(false);
            var deliveryPayloadBytes = await GetObjectBytesAsync(client, bucket, deliveryPayloadKey).ConfigureAwait(false);

            CollectionAssert.AreEqual(payload, archivePayloadBytes, "Archive payload should mirror exported bytes.");
            CollectionAssert.AreEqual(payload, deliveryPayloadBytes, "Delivery payload should mirror exported bytes.");

            var archiveManifestJson = await GetObjectStringAsync(client, bucket, archiveManifestKey).ConfigureAwait(false);
            var deliveryManifestJson = await GetObjectStringAsync(client, bucket, deliveryManifestKey).ConfigureAwait(false);

            var expectedJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.AreEqual(expectedJson, archiveManifestJson, "Archive manifest should match serialized metadata.");
            Assert.AreEqual(expectedJson, deliveryManifestJson, "Delivery manifest should match serialized metadata.");
        }
        finally
        {
            await CleanupObjectAsync(client, bucket, archivePayloadKey).ConfigureAwait(false);
            await CleanupObjectAsync(client, bucket, archiveManifestKey).ConfigureAwait(false);
            await CleanupObjectAsync(client, bucket, deliveryPayloadKey).ConfigureAwait(false);
            await CleanupObjectAsync(client, bucket, deliveryManifestKey).ConfigureAwait(false);
            await CleanupBucketAsync(client, bucket).ConfigureAwait(false);
        }
    }

    private async Task CleanupObjectAsync(IMinioClient client, string bucket, string objectKey)
    {
        try
        {
            await client.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(bucket).WithObject(objectKey), CancellationToken.None).ConfigureAwait(false);
        }
        catch (MinioException ex)
        {
            TestContext?.WriteLine($"[cleanup] remove object {bucket}/{objectKey} failed: {ex.Message}");
        }
    }

    private async Task CleanupBucketAsync(IMinioClient client, string bucket)
    {
        try
        {
            await client.RemoveBucketAsync(new RemoveBucketArgs().WithBucket(bucket), CancellationToken.None).ConfigureAwait(false);
        }
        catch (MinioException ex)
        {
            TestContext?.WriteLine($"[cleanup] remove bucket {bucket} failed: {ex.Message}");
        }
    }

    private static T? GetPropertyValue<T>(object target, string propertyName)
    {
        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        var property = target.GetType().GetProperty(propertyName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (property is null)
        {
            return default;
        }

        var value = property.GetValue(target);
        if (value is null)
        {
            return default;
        }

        return (T)value;
    }

    private static string? GetHeaderValue(IDictionary<string, string>? headers, string suffix)
    {
        if (headers is null)
        {
            return null;
        }

        foreach (var (key, value) in headers)
        {
            if (key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }

    private static async Task<byte[]> GetObjectBytesAsync(IMinioClient client, string bucket, string objectKey)
    {
        using var buffer = new MemoryStream();
        var getArgs = new GetObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectKey)
            .WithCallbackStream(async (stream, token) =>
            {
                if (stream.CanSeek)
                {
                    stream.Position = 0;
                }

                await stream.CopyToAsync(buffer, token).ConfigureAwait(false);
                buffer.Position = 0;
            });

        await client.GetObjectAsync(getArgs, CancellationToken.None).ConfigureAwait(false);
        return buffer.ToArray();
    }

    private static async Task<string> GetObjectStringAsync(IMinioClient client, string bucket, string objectKey)
        => Encoding.UTF8.GetString(await GetObjectBytesAsync(client, bucket, objectKey).ConfigureAwait(false));
}
