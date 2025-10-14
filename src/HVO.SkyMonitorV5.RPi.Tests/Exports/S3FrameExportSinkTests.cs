using System;
using System.Collections.Generic;
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
    var resilienceProvider = new TestResiliencePolicyProvider();

        clientProvider
            .Setup(provider => provider.GetClient("play.min.io", "access", "secret", true))
            .Returns(minioClient.Object);

        minioClient
            .Setup(client => client.BucketExistsAsync(It.IsAny<BucketExistsArgs>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        minioClient
            .Setup(client => client.PutObjectAsync(It.IsAny<PutObjectArgs>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectArgs, CancellationToken>((args, _) => capturedCalls.Add(args))
            .Returns(Task.FromResult(default(PutObjectResponse)!));

        var sink = new S3FrameExportSink(
            FrameExportStage.Raw,
            optionsMonitor,
            clientProvider.Object,
            resilienceProvider,
            NullLogger<S3FrameExportSink>.Instance);

        var frameId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2025, 10, 13, 12, 34, 56, TimeSpan.Zero);
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
            FullPipelineMilliseconds: 1615.6);

        var payload = new ReadOnlyMemory<byte>(new byte[] { 5, 4, 3, 2, 1 });
        var envelope = new FrameExportEnvelope(
            frameId,
            FrameExportStage.Raw,
            metadata,
            payload,
            "image/png",
            "png");

        var result = await sink.ExportAsync(envelope, CancellationToken.None);

        Assert.IsTrue(result.IsSuccessful, "Expected upload to succeed.");
        Assert.IsTrue(result.Value, "Expected sink to report persistence.");
        Assert.AreEqual(2, capturedCalls.Count, "Expected payload and manifest uploads.");

        clientProvider.VerifyAll();
        minioClient.VerifyAll();
    }
}
