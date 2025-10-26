using HVO.SkyMonitorV5.RPi.Controllers.v1_0;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace HVO.SkyMonitorV5.RPi.Tests.Controllers;

[TestClass]
public sealed class DiagnosticsController_FrameExportTests
{
  [TestMethod]
  public void GetFrameExportConfiguration_ReturnsEffectiveOptions()
  {
    var options = new FrameExportOptions
    {
      Raw = new FrameExportStageOptions
      {
        Enabled = true,
        PayloadScope = FrameExportPayloadScope.ArchiveOnly,
        ArchiveEncoding = new ImageEncodingSettings(ImageEncodingFormat.Fits, 100)
        {
          FitsOptions = new FitsEncodingOptions
          {
            BitDepth = global::HVO.SkyMonitorV5.RPi.Pipeline.FitsBitDepth.U16,
            ImageFormat = global::HVO.SkyMonitorV5.RPi.Pipeline.FitsImageFormat.Mono,
            Compression = global::HVO.SkyMonitorV5.RPi.Pipeline.FitsCompression.None,
            UnsignedU16 = true,
            WriteChecksum = true
          }
        },
        DeliveryEncoding = new ImageEncodingSettings(ImageEncodingFormat.Jpeg, 80)
      },
      Processed = new FrameExportStageOptions
      {
        Enabled = true,
        PayloadScope = FrameExportPayloadScope.ArchiveOnly,
        ArchiveEncoding = new ImageEncodingSettings(ImageEncodingFormat.Jpeg, 95)
      }
    };

    var optionsMonitor = Mock.Of<IOptionsMonitor<FrameExportOptions>>(m => m.CurrentValue == options);
    var diagnosticsService = Mock.Of<IDiagnosticsService>();

    var controller = new DiagnosticsController(diagnosticsService, optionsMonitor, NullLogger<DiagnosticsController>.Instance)
    {
      ControllerContext = new ControllerContext
      {
        HttpContext = new DefaultHttpContext()
      }
    };

    var actionResult = controller.GetFrameExportConfiguration();
    Assert.IsNotNull(actionResult, "Expected ActionResult.");

    var ok = actionResult.Result as OkObjectResult;
    Assert.IsNotNull(ok, "Expected OK response.");

    var payload = ok.Value as DiagnosticsFrameExportResponse;
    Assert.IsNotNull(payload, "Expected DiagnosticsFrameExportResponse payload.");

    // Raw stage assertions
    Assert.IsTrue(payload.Raw.Enabled, "Raw should be enabled.");
    Assert.AreEqual("ArchiveOnly", payload.Raw.PayloadScope, "Scope should be ArchiveOnly.");
    Assert.AreEqual("Fits", payload.Raw.Archive.Format, "Raw archive format should be FITS.");
    Assert.AreEqual("image/fits", payload.Raw.Archive.ContentType, "FITS content type should be image/fits.");
    Assert.AreEqual("fits", payload.Raw.Archive.FileExtension, "FITS extension should be .fits.");
    Assert.IsFalse(payload.Raw.Archive.IsRaster, "FITS should not be raster.");
    Assert.IsNotNull(payload.Raw.Archive.Fits, "FITS details should be present.");

    Assert.AreEqual("Jpeg", payload.Raw.Delivery.Format, "Raw delivery format should be JPEG.");
    Assert.AreEqual("image/jpeg", payload.Raw.Delivery.ContentType, "JPEG content type should be image/jpeg.");
    Assert.AreEqual("jpg", payload.Raw.Delivery.FileExtension, "JPEG extension should be .jpg.");
    Assert.IsTrue(payload.Raw.Delivery.IsRaster, "JPEG should be raster.");

    // Processed stage assertions
    Assert.IsTrue(payload.Processed.Enabled, "Processed should be enabled.");
    Assert.AreEqual("Jpeg", payload.Processed.Archive.Format, "Processed archive format should be JPEG.");
    Assert.AreEqual("image/jpeg", payload.Processed.Archive.ContentType, "JPEG content type should be image/jpeg.");
    Assert.AreEqual("jpg", payload.Processed.Archive.FileExtension, "JPEG extension should be .jpg.");
    Assert.IsTrue(payload.Processed.Archive.IsRaster, "JPEG should be raster.");
  }

  [TestMethod]
  public void GetFrameExportConfiguration_ReportsMimeAndRaster_ForTiffAndXisf()
  {
    var options = new FrameExportOptions
    {
      Raw = new FrameExportStageOptions
      {
        Enabled = true,
        PayloadScope = FrameExportPayloadScope.ArchiveOnly,
        ArchiveEncoding = new ImageEncodingSettings(ImageEncodingFormat.Tiff, 90),
        DeliveryEncoding = new ImageEncodingSettings(ImageEncodingFormat.Xisf, 100)
      },
      Processed = new FrameExportStageOptions
      {
        Enabled = true,
        PayloadScope = FrameExportPayloadScope.DeliveryOnly,
        ArchiveEncoding = new ImageEncodingSettings(ImageEncodingFormat.Tiff, 90),
        DeliveryEncoding = new ImageEncodingSettings(ImageEncodingFormat.Xisf, 100)
      }
    };

    var optionsMonitor = Mock.Of<IOptionsMonitor<FrameExportOptions>>(m => m.CurrentValue == options);
    var diagnosticsService = Mock.Of<IDiagnosticsService>();

    var controller = new DiagnosticsController(diagnosticsService, optionsMonitor, NullLogger<DiagnosticsController>.Instance)
    {
      ControllerContext = new ControllerContext
      {
        HttpContext = new DefaultHttpContext()
      }
    };

    var result = controller.GetFrameExportConfiguration();
    Assert.IsNotNull(result);
    var ok = result.Result as OkObjectResult;
    Assert.IsNotNull(ok);
    var payload = ok.Value as DiagnosticsFrameExportResponse;
    Assert.IsNotNull(payload);

    // Raw: TIFF archive should be raster with image/tiff and .tiff; XISF delivery should be non-raster with octet-stream and .xisf
    Assert.AreEqual("Tiff", payload.Raw.Archive.Format);
    Assert.AreEqual("image/tiff", payload.Raw.Archive.ContentType);
    Assert.AreEqual("tiff", payload.Raw.Archive.FileExtension);
    Assert.IsTrue(payload.Raw.Archive.IsRaster);

    Assert.AreEqual("Xisf", payload.Raw.Delivery.Format);
    Assert.AreEqual("application/octet-stream", payload.Raw.Delivery.ContentType);
    Assert.AreEqual("xisf", payload.Raw.Delivery.FileExtension);
    Assert.IsFalse(payload.Raw.Delivery.IsRaster);

    // Processed mirrors the same MIME/extension/raster semantics
    Assert.AreEqual("Tiff", payload.Processed.Archive.Format);
    Assert.AreEqual("image/tiff", payload.Processed.Archive.ContentType);
    Assert.AreEqual("tiff", payload.Processed.Archive.FileExtension);
    Assert.IsTrue(payload.Processed.Archive.IsRaster);

    Assert.AreEqual("Xisf", payload.Processed.Delivery.Format);
    Assert.AreEqual("application/octet-stream", payload.Processed.Delivery.ContentType);
    Assert.AreEqual("xisf", payload.Processed.Delivery.FileExtension);
    Assert.IsFalse(payload.Processed.Delivery.IsRaster);
  }

  [TestMethod]
  public void GetFrameExportConfiguration_FitsForProcessed_IncludesFitsDetails()
  {
    var options = new FrameExportOptions
    {
      Raw = new FrameExportStageOptions
      {
        Enabled = true,
        PayloadScope = FrameExportPayloadScope.ArchiveOnly,
        ArchiveEncoding = new ImageEncodingSettings(ImageEncodingFormat.Fits, 100)
        {
          FitsOptions = new FitsEncodingOptions
          {
            BitDepth = global::HVO.SkyMonitorV5.RPi.Pipeline.FitsBitDepth.U16,
            ImageFormat = global::HVO.SkyMonitorV5.RPi.Pipeline.FitsImageFormat.Mono,
            Compression = global::HVO.SkyMonitorV5.RPi.Pipeline.FitsCompression.None,
            UnsignedU16 = true,
            WriteChecksum = true
          }
        },
        DeliveryEncoding = new ImageEncodingSettings(ImageEncodingFormat.Jpeg, 80)
      },
      Processed = new FrameExportStageOptions
      {
        Enabled = true,
        PayloadScope = FrameExportPayloadScope.ArchiveOnly,
        ArchiveEncoding = new ImageEncodingSettings(ImageEncodingFormat.Fits, 100)
        {
          FitsOptions = new FitsEncodingOptions
          {
            BitDepth = global::HVO.SkyMonitorV5.RPi.Pipeline.FitsBitDepth.U16,
            ImageFormat = global::HVO.SkyMonitorV5.RPi.Pipeline.FitsImageFormat.Mono,
            Compression = global::HVO.SkyMonitorV5.RPi.Pipeline.FitsCompression.None,
            UnsignedU16 = true,
            WriteChecksum = true
          }
        }
      }
    };

    var optionsMonitor = Mock.Of<IOptionsMonitor<FrameExportOptions>>(m => m.CurrentValue == options);
    var diagnosticsService = Mock.Of<IDiagnosticsService>();

    var controller = new DiagnosticsController(diagnosticsService, optionsMonitor, NullLogger<DiagnosticsController>.Instance)
    {
      ControllerContext = new ControllerContext
      {
        HttpContext = new DefaultHttpContext()
      }
    };

    var actionResult = controller.GetFrameExportConfiguration();
    Assert.IsNotNull(actionResult);
    var ok = actionResult.Result as OkObjectResult;
    Assert.IsNotNull(ok);
    var payload = ok.Value as DiagnosticsFrameExportResponse;
    Assert.IsNotNull(payload);

    // Raw FITS assertions
    Assert.AreEqual("Fits", payload.Raw.Archive.Format);
    Assert.AreEqual("image/fits", payload.Raw.Archive.ContentType);
    Assert.AreEqual("fits", payload.Raw.Archive.FileExtension);
    Assert.IsFalse(payload.Raw.Archive.IsRaster);
    Assert.IsNotNull(payload.Raw.Archive.Fits);

    // Processed FITS assertions
    Assert.AreEqual("Fits", payload.Processed.Archive.Format);
    Assert.AreEqual("image/fits", payload.Processed.Archive.ContentType);
    Assert.AreEqual("fits", payload.Processed.Archive.FileExtension);
    Assert.IsFalse(payload.Processed.Archive.IsRaster);
    Assert.IsNotNull(payload.Processed.Archive.Fits);
  }
}
