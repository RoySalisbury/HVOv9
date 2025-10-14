using System;
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
using Minio.Exceptions;

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
        var sink = new S3FrameExportSink(
            FrameExportStage.Raw,
            optionsMonitor,
            provider,
            resilienceProvider,
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
            RawImageDescriptor: descriptor);

        var payload = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var envelope = new FrameExportEnvelope(
            frameId,
            FrameExportStage.Raw,
            metadata,
            payload.AsMemory(),
            "application/vnd.hvo.skia.raw",
            "skimg");

        var prefixPath = options.Raw.S3[0].BuildObjectPrefix(FrameExportStage.Raw, timestampUtc);
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

            await client.StatObjectAsync(new StatObjectArgs().WithBucket(bucket).WithObject(payloadKey), CancellationToken.None).ConfigureAwait(false);
            await client.StatObjectAsync(new StatObjectArgs().WithBucket(bucket).WithObject(manifestKey), CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            await CleanupObjectAsync(client, bucket, payloadKey).ConfigureAwait(false);
            await CleanupObjectAsync(client, bucket, manifestKey).ConfigureAwait(false);
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
}
