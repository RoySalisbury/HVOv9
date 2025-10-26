using System;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Services.RemoteDispatch;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Minio;
using Minio.DataModel.Args;
using Minio.DataModel.Response;
using Minio.Exceptions;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Tests.Services.RemoteDispatch;

[TestClass]
public sealed class RemoteFramePublisherTests
{
    [TestMethod]
    public async Task PublishAsync_RemoteDisabled_ReturnsDisabled()
    {
        var options = new CameraPipelineOptions
        {
            RemoteDispatch = new RemoteDispatchOptions
            {
                Enabled = false
            }
        };

        using var fixture = CreateEnvelopeFixture();
    var publisher = CreatePublisher(options, CreateUnusedFactory(), CreateEncoder());

        var result = await publisher.PublishAsync(fixture.Envelope, CancellationToken.None);

        Assert.AreEqual(RemoteDispatchOutcome.Disabled, result.Outcome);
    }

    [TestMethod]
    public async Task PublishAsync_S3WithoutBucket_ReturnsFailure()
    {
        var options = new CameraPipelineOptions
        {
            RemoteDispatch = new RemoteDispatchOptions
            {
                Enabled = true,
                Mode = RemoteDispatchMode.S3,
                S3Bucket = null
            }
        };

        using var fixture = CreateEnvelopeFixture();
    var publisher = CreatePublisher(options, CreateUnusedFactory(), CreateEncoder());

        var result = await publisher.PublishAsync(fixture.Envelope, CancellationToken.None);

        Assert.AreEqual(RemoteDispatchOutcome.Failed, result.Outcome);
        StringAssert.Contains(result.Message!, "bucket");
    }

    [TestMethod]
    public async Task PublishAsync_S3WithBucket_ReturnsSuccess()
    {
        var options = new CameraPipelineOptions
        {
            RemoteDispatch = new RemoteDispatchOptions
            {
                Enabled = true,
                Mode = RemoteDispatchMode.S3,
                S3Bucket = "hvo-test-bucket",
                FanoutExchange = "sky-monitor",
                Endpoint = "play.min.io",
                AccessKey = "access",
                SecretKey = "secret",
                UseSsl = true
            }
        };

        using var fixture = CreateEnvelopeFixture();
        var factoryMock = new Mock<IMinioClientProvider>(MockBehavior.Strict);
        var minioMock = new Mock<IMinioClient>(MockBehavior.Strict);
        PutObjectArgs? capturedArgs = null;

        factoryMock
            .Setup(factory => factory.GetClient("play.min.io", "access", "secret", true))
            .Returns(minioMock.Object);

        minioMock
            .Setup(client => client.PutObjectAsync(It.IsAny<PutObjectArgs>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectArgs, CancellationToken>((args, _) => capturedArgs = args)
            .Returns(Task.FromResult(default(PutObjectResponse)!));

    var publisher = CreatePublisher(options, factoryMock.Object, CreateEncoder());

        var result = await publisher.PublishAsync(fixture.Envelope, CancellationToken.None);

        Assert.AreEqual(RemoteDispatchOutcome.Succeeded, result.Outcome);
        StringAssert.Contains(result.Message!, "hvo-test-bucket");
        Assert.IsNotNull(capturedArgs, "Expected MinIO PutObjectAsync to be invoked.");

        factoryMock.VerifyAll();
        minioMock.VerifyAll();
    }

    [TestMethod]
    public async Task PublishAsync_S3UploadThrows_ReturnsFailure()
    {
        var options = new CameraPipelineOptions
        {
            RemoteDispatch = new RemoteDispatchOptions
            {
                Enabled = true,
                Mode = RemoteDispatchMode.S3,
                S3Bucket = "hvo-test-bucket",
                Endpoint = "play.min.io",
                AccessKey = "access",
                SecretKey = "secret",
                UseSsl = true
            }
        };

        using var fixture = CreateEnvelopeFixture();
        var factoryMock = new Mock<IMinioClientProvider>(MockBehavior.Strict);
        var minioMock = new Mock<IMinioClient>(MockBehavior.Strict);

        factoryMock
            .Setup(factory => factory.GetClient("play.min.io", "access", "secret", true))
            .Returns(minioMock.Object);

        minioMock
            .Setup(client => client.PutObjectAsync(It.IsAny<PutObjectArgs>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MinioException("simulated"));

    var publisher = CreatePublisher(options, factoryMock.Object, CreateEncoder());

        var result = await publisher.PublishAsync(fixture.Envelope, CancellationToken.None);

        Assert.AreEqual(RemoteDispatchOutcome.Failed, result.Outcome);
        StringAssert.Contains(result.Message!, "rejected");

        factoryMock.VerifyAll();
        minioMock.VerifyAll();
    }

    private static RemoteFramePublisher CreatePublisher(CameraPipelineOptions options, IMinioClientProvider factory, IRemoteFrameEncoder encoder)
    {
        var monitor = new TestOptionsMonitor(options);
        return new RemoteFramePublisher(monitor, NullLogger<RemoteFramePublisher>.Instance, factory, encoder);
    }

    private static IMinioClientProvider CreateUnusedFactory()
    {
        var factoryMock = new Mock<IMinioClientProvider>(MockBehavior.Strict);
        return factoryMock.Object;
    }

    private static IRemoteFrameEncoder CreateEncoder()
        => new SkiaRemoteFrameEncoder(NullLogger<SkiaRemoteFrameEncoder>.Instance);

    private static EnvelopeFixture CreateEnvelopeFixture()
        => new();

    private sealed class TestOptionsMonitor : IOptionsMonitor<CameraPipelineOptions>, IDisposable
    {
        private CameraPipelineOptions _value;

        public TestOptionsMonitor(CameraPipelineOptions value)
        {
            _value = value;
        }

        public CameraPipelineOptions CurrentValue => _value;

        public CameraPipelineOptions Get(string? name) => _value;

        public IDisposable OnChange(Action<CameraPipelineOptions, string?> listener)
        {
            // No change tracking required for tests
            return this;
        }

        public void Dispose()
        {
        }
    }

    private sealed class EnvelopeFixture : IDisposable
    {
        private readonly SKBitmap _bitmap;

        public EnvelopeFixture()
        {
            _bitmap = new SKBitmap(4, 4);
            var capturedImage = new CapturedImage(Guid.NewGuid(), _bitmap, DateTimeOffset.UtcNow, new ExposureSettings(1000, 100, false, false), null);
            var configuration = CameraConfiguration.FromOptions(new CameraPipelineOptions());
            var rig = RigPresets.MockAsi174_Fujinon;

            Envelope = new RemoteFrameEnvelope(
                FrameNumber: 1,
                CapturedFrame: capturedImage,
                Rig: rig,
                Configuration: configuration,
                ConfigurationVersion: 1,
                UsingBackgroundStacker: true,
                CaptureMilliseconds: 250,
                CapturedAtLocal: DateTimeOffset.Now,
                CapturedAtUtc: DateTimeOffset.UtcNow);
        }

        public RemoteFrameEnvelope Envelope { get; }

        public void Dispose()
        {
            _bitmap.Dispose();
        }
    }
}
