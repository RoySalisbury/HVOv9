using System;
using System.Globalization;
using HVO.SkyMonitorV5.RPi.Controllers.v1_0;
using HVO.SkyMonitorV5.RPi.Exports;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Skia;
using HVO.SkyMonitorV5.RPi.Services;
using HVO.SkyMonitorV5.RPi.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Tests.Controllers;

[TestClass]
public sealed class AllSkyControllerTests
{
    [TestMethod]
    public void GetLatestFrame_WithRawFormatPng_ReturnsPngWithDescriptorHeaders()
    {
        using var context = new RawFrameTestContext();
        var controller = CreateController(context.Frame);

        var result = controller.GetLatestFrame(raw: true, rawFormat: "png") as FileContentResult;

        Assert.IsNotNull(result, "Controller should return a file result for raw frames.");
        Assert.AreEqual("image/png", result.ContentType, "PNG format should be returned when requested.");
        Assert.IsNotEmpty(result.FileContents, "PNG payload should not be empty.");

        var headers = controller.Response.Headers;
        Assert.AreEqual(context.Descriptor.Width.ToString(CultureInfo.InvariantCulture), headers["X-HVO-Raw-Width"].ToString(), "Width header should match descriptor.");
        Assert.AreEqual(context.Descriptor.Height.ToString(CultureInfo.InvariantCulture), headers["X-HVO-Raw-Height"].ToString(), "Height header should match descriptor.");
        Assert.AreEqual(context.Descriptor.BytesPerPixel.ToString(CultureInfo.InvariantCulture), headers["X-HVO-Raw-BytesPerPixel"].ToString(), "Bytes-per-pixel header should match descriptor.");
        Assert.AreEqual(context.Descriptor.PixelFormatHint, headers["X-HVO-Raw-PixelFormat"].ToString(), "Pixel format hint should be emitted.");
    }

    [TestMethod]
    public void GetLatestFrame_DefaultRawFormat_ReturnsRawPayload()
    {
        using var context = new RawFrameTestContext();
        var controller = CreateController(context.Frame);

        var result = controller.GetLatestFrame(raw: true) as FileContentResult;

        Assert.IsNotNull(result, "Controller should return a file result for raw frames.");
        Assert.AreEqual(SkiaRawFrameHelper.RawContentType, result.ContentType, "Raw content type should be preserved when format not specified.");
        Assert.HasCount(context.ExpectedRawPayloadLength, result.FileContents, "Raw payload length should match descriptor dimensions.");

        var headers = controller.Response.Headers;
        Assert.AreEqual(context.Descriptor.PixelFormatHint, headers["X-HVO-Raw-PixelFormat"].ToString(), "Pixel format hint should be emitted for raw payloads.");
    }

    [TestMethod]
    public void GetLatestFrame_WithExplicitRawFormat_ReturnsRawPayload()
    {
        using var context = new RawFrameTestContext();
        var controller = CreateController(context.Frame);

        var result = controller.GetLatestFrame(raw: true, rawFormat: "raw") as FileContentResult;

        Assert.IsNotNull(result, "Controller should return a file result for raw frames.");
        Assert.AreEqual(SkiaRawFrameHelper.RawContentType, result.ContentType, "Raw content type should be preserved when explicitly requested.");
        Assert.HasCount(context.ExpectedRawPayloadLength, result.FileContents, "Raw payload length should match descriptor dimensions.");
    }

    [TestMethod]
    public void GetLatestFrame_WithFitsFormat_ReturnsFitsWhenEnabled()
    {
        using var context = new RawFrameTestContext();

        var fitsBytes = new byte[] { 0x01, 0x02, 0x03 };
        var fitsEncoder = new Mock<IFitsFrameEncoder>(MockBehavior.Strict);
        fitsEncoder
            .Setup(e => e.EncodeRaw(It.IsAny<SKImage>(), It.IsAny<RawFrameSnapshot>(), It.IsAny<HVO.SkyMonitorV5.RPi.Cameras.Projection.RigSpec>(), It.IsAny<FitsExportOptions>()))
            .Returns(new ProcessedFrameDelivery(fitsBytes, "application/fits", "fits"));

        var controller = CreateController(context.Frame, enableFits: true, fitsEncoder: fitsEncoder.Object);
        var result = controller.GetLatestFrame(raw: true, rawFormat: "fits") as FileContentResult;

        Assert.IsNotNull(result, "Controller should return a file for FITS request.");
        Assert.AreEqual("application/fits", result.ContentType, "FITS content type should be returned when requested and enabled.");
        Assert.HasCount(fitsBytes.Length, result.FileContents, "FITS payload should match encoder output.");
    }

    private static AllSkyController CreateController(RawFrameSnapshot frame, bool enableFits = false, IFitsFrameEncoder? fitsEncoder = null)
    {
        var frameStateStore = new Mock<IFrameStateStore>();
        frameStateStore.SetupGet(store => store.LatestRawFrame).Returns(frame);
        frameStateStore.SetupGet(store => store.Configuration).Returns(CameraConfiguration.FromOptions(new CameraPipelineOptions()));

        var optionsMonitor = new Mock<IOptionsMonitor<CameraPipelineOptions>>();
        optionsMonitor.SetupGet(options => options.CurrentValue).Returns(new CameraPipelineOptions());

        var encoder = new Mock<IProcessedFrameEncoder>();

        var fitsOptions = new Mock<IOptionsMonitor<FitsExportOptions>>();
        fitsOptions.SetupGet(o => o.CurrentValue).Returns(new FitsExportOptions { EnableForRaw = enableFits, EnableForProcessed = false });
        var rigAdapter = new Mock<HVO.SkyMonitorV5.RPi.Cameras.Acquisition.IRigAcquisitionAdapter>();
        rigAdapter.SetupGet(r => r.ActiveRig).Returns(HVO.SkyMonitorV5.RPi.Cameras.Projection.RigPresets.MockAsi174_Fujinon);

        var controller = new AllSkyController(
            frameStateStore.Object,
            optionsMonitor.Object,
            encoder.Object,
            fitsEncoder ?? Mock.Of<IFitsFrameEncoder>(),
            rigAdapter.Object,
            fitsOptions.Object,
            NullLogger<AllSkyController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        return controller;
    }

    private sealed class RawFrameTestContext : IDisposable
    {
        private readonly SKSurface _surface;
        private readonly SKImage _image;
        private readonly SKBitmap _bitmap;

        public RawFrameSnapshot Frame { get; }
        public FrameExportImageDescriptor Descriptor { get; }
        public int ExpectedRawPayloadLength => Descriptor.RowBytes * Descriptor.Height;

        public RawFrameTestContext()
        {
            var info = new SKImageInfo(6, 4, SKColorType.Bgra8888, SKAlphaType.Premul);
            _surface = SKSurface.Create(info) ?? throw new AssertFailedException("Failed to allocate test surface.");
            _surface.Canvas.Clear(new SKColor(64, 128, 192, 255));

            _image = _surface.Snapshot() ?? throw new AssertFailedException("Snapshot creation failed.");
            _bitmap = SKBitmap.FromImage(_image) ?? throw new AssertFailedException("Bitmap clone from snapshot failed.");

            Descriptor = SkiaRawFrameHelper.TryCreateDescriptor(_image) ?? throw new AssertFailedException("Descriptor should be produced for snapshot.");

            Frame = new RawFrameSnapshot(Guid.NewGuid(), _bitmap, DateTimeOffset.UtcNow, new ExposureSettings(1_500, 200, false, false))
            {
                ImmutableImage = _image,
                ImageDescriptor = Descriptor
            };
        }

        public void Dispose()
        {
            _image.Dispose();
            _bitmap.Dispose();
            _surface.Dispose();
        }
    }
}
