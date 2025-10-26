using System;
using HVO.Astronomy.CFITSIO;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Pipeline;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Services;

/// <summary>
/// Encodes all-sky frames to FITS format with rich astronomical metadata.
/// </summary>
public sealed class FitsFrameEncoder : IFitsFrameEncoder
{
  private readonly ILogger<FitsFrameEncoder> _logger;
  private readonly ObservatoryLocationOptions _siteOptions;

  public FitsFrameEncoder(
      ILogger<FitsFrameEncoder> logger,
      IOptions<ObservatoryLocationOptions> siteOptions)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _siteOptions = siteOptions?.Value ?? throw new ArgumentNullException(nameof(siteOptions));
  }

  public ProcessedFrameDelivery EncodeRaw(SKImage image, RawFrameSnapshot frame, RigSpec rig, FitsEncodingOptions? options)
  {
    ArgumentNullException.ThrowIfNull(image);
    ArgumentNullException.ThrowIfNull(frame);
    ArgumentNullException.ThrowIfNull(rig);

    try
    {
      using var bitmap = SKBitmap.FromImage(image);
      var compressionPolicy = CreateCompressionPolicy(options);

      var fitsBytes = bitmap.ToFitsU16BytesResult(
          compressionPolicy: compressionPolicy,
          stampHeader: fits => StampRawFrameHeaders(fits, frame, rig, options));

      if (fitsBytes.IsFailure)
      {
        _logger.LogError(fitsBytes.Error, "Failed to encode raw frame {FrameId} to FITS", frame.FrameId);
        throw fitsBytes.Error!;
      }

      _logger.LogDebug("Encoded raw frame {FrameId} to FITS ({Size} bytes)", frame.FrameId, fitsBytes.Value.Length);

      return new ProcessedFrameDelivery(
          Payload: fitsBytes.Value,
          ContentType: "application/fits",
          FileExtension: "fits");
    }
    catch (Exception ex) when (ex is not ArgumentNullException)
    {
      _logger.LogError(ex, "Error encoding raw frame {FrameId} to FITS", frame.FrameId);
      throw;
    }
  }

  public ProcessedFrameDelivery EncodeProcessed(ProcessedFrame frame, RigSpec rig, FitsEncodingOptions? options)
  {
    ArgumentNullException.ThrowIfNull(frame);
    ArgumentNullException.ThrowIfNull(rig);

    try
    {
      using var bitmap = SKBitmap.FromImage(frame.ImmutableImage);
      var compressionPolicy = CreateCompressionPolicy(options);

      var fitsBytes = bitmap.ToFitsU16BytesResult(
          compressionPolicy: compressionPolicy,
          stampHeader: fits => StampProcessedFrameHeaders(fits, frame, rig, options));

      if (fitsBytes.IsFailure)
      {
        _logger.LogError(fitsBytes.Error, "Failed to encode processed frame {FrameId} to FITS", frame.FrameId);
        throw fitsBytes.Error!;
      }

      _logger.LogDebug("Encoded processed frame {FrameId} to FITS ({Size} bytes, {FiltersCount} filters applied)",
          frame.FrameId, fitsBytes.Value.Length, frame.AppliedFilters.Count);

      return new ProcessedFrameDelivery(
          Payload: fitsBytes.Value,
          ContentType: "application/fits",
          FileExtension: "fits");
    }
    catch (Exception ex) when (ex is not ArgumentNullException)
    {
      _logger.LogError(ex, "Error encoding processed frame {FrameId} to FITS", frame.FrameId);
      throw;
    }
  }

  private FitsCompressionPolicy? CreateCompressionPolicy(FitsEncodingOptions? options)
  {
    if (options is null || options.Compression == Pipeline.FitsCompression.None)
    {
      return null;
    }

    var compression = options.Compression switch
    {
      Pipeline.FitsCompression.Rice => HVO.Astronomy.CFITSIO.FitsCompression.Rice,
      Pipeline.FitsCompression.Gzip1 => HVO.Astronomy.CFITSIO.FitsCompression.GZip1,
      Pipeline.FitsCompression.Gzip2 => HVO.Astronomy.CFITSIO.FitsCompression.GZip2,
      Pipeline.FitsCompression.HCompress => HVO.Astronomy.CFITSIO.FitsCompression.HCompress,
      Pipeline.FitsCompression.PLio => HVO.Astronomy.CFITSIO.FitsCompression.None,
      _ => HVO.Astronomy.CFITSIO.FitsCompression.None
    };

    return new FitsCompressionPolicy
    {
      Compression = compression,
      WriteChecksum = options.WriteChecksum
    };
  }

  private void StampRawFrameHeaders(FitsFile fits, RawFrameSnapshot frame, RigSpec rig, FitsEncodingOptions? options)
  {
    var builder = new FitsHeaderBuilder(fits);

    // Core image metadata
    StampImageMetadata(builder, frame.Image.Width, frame.Image.Height, options);

    // Timing
    StampTimingMetadata(builder, frame.Timestamp);

    // Camera/Instrument
    StampInstrumentMetadata(builder, rig, frame.Exposure);

    // Observatory site
    StampSiteMetadata(builder);

    // Processing notes
    builder.SetString("IMAGETYP", "Light Frame", "Type of image")
           .SetString("SWCREATE", "HVO SkyMonitor V5", "Software that created this file")
           .SetString("ORIGIN", "Hualapai Valley Observatory", "Organization responsible for the data");
  }

  private void StampProcessedFrameHeaders(FitsFile fits, ProcessedFrame frame, RigSpec rig, FitsEncodingOptions? options)
  {
    var builder = new FitsHeaderBuilder(fits);

    // Core image metadata
    StampImageMetadata(builder, frame.ImmutableImage.Width, frame.ImmutableImage.Height, options);

    // Timing
    StampTimingMetadata(builder, frame.Timestamp);

    // Camera/Instrument
    StampInstrumentMetadata(builder, rig, frame.Exposure);

    // Observatory site
    StampSiteMetadata(builder);

    // Processing metadata
    builder.SetString("IMAGETYP", "Processed", "Type of image")
           .SetInt32("NCOMBINE", frame.FramesStacked, "Number of frames combined")
           .SetDouble("INTTIME", frame.IntegrationMilliseconds / 1000.0, 3, "Total integration time (s)")
           .SetString("SWCREATE", "HVO SkyMonitor V5", "Software that created this file")
           .SetString("ORIGIN", "Hualapai Valley Observatory", "Organization responsible for the data");

    // Applied filters
    if (frame.AppliedFilters.Count > 0)
    {
      builder.SetString("FILTERS", string.Join(", ", frame.AppliedFilters), "Image processing filters applied");
    }
  }

  private void StampImageMetadata(FitsHeaderBuilder builder, int width, int height, FitsEncodingOptions? options)
  {
    if (options is not null && options.UnsignedU16 && options.BitDepth == Pipeline.FitsBitDepth.U16)
    {
      // BSCALE=1, BZERO=32768 for unsigned 16-bit
      builder.SetScale(1.0, 32768.0);
    }

    builder.SetString("BUNIT", "ADU", "Physical units of the image data");
  }

  private void StampTimingMetadata(FitsHeaderBuilder builder, DateTimeOffset timestamp)
  {
    var utc = timestamp.UtcDateTime;
    builder.SetString("TIMESYS", "UTC", "Time system")
           .SetDateObs(utc);

    // Calculate MJD-OBS (Modified Julian Date)
    var mjd = CalculateMjd(utc);
    builder.SetDouble("MJD-OBS", mjd, 8, "Modified Julian Date at start");
  }

  private void StampInstrumentMetadata(FitsHeaderBuilder builder, RigSpec rig, ExposureSettings exposure)
  {
    // Camera and rig
    builder.SetString("INSTRUME", rig.Camera.Descriptor.Model, "Camera/Detector model")
           .SetString("TELESCOP", rig.Name, "Telescope/Rig designation");

    // Exposure settings
    builder.SetExposureSeconds(exposure.ExposureMilliseconds / 1000.0)
           .SetInt32("GAIN", exposure.Gain, "Sensor gain setting");

    // Sensor geometry
    var sensor = rig.Sensor;
    builder.SetDouble("XPIXSZ", sensor.PixelSizeMicrons, 3, "Pixel width (microns)")
           .SetDouble("YPIXSZ", sensor.PixelSizeMicrons, 3, "Pixel height (microns)")
           .SetInt32("XBINNING", 1, "Binning factor X")
           .SetInt32("YBINNING", 1, "Binning factor Y");

    // Lens/optics
    builder.SetDouble("FOCALLEN", rig.Lens.FocalLengthMm, 3, "Focal length (mm)");

    // Calculate pixel scale if possible
    var pixelScale = CalculatePixelScale(sensor.PixelSizeMicrons, rig.Lens.FocalLengthMm);
    if (pixelScale > 0)
    {
      builder.SetDouble("PIXSCALE", pixelScale, 3, "Pixel scale (arcsec/pixel)");
    }

    // Boresight pointing
    builder.SetDouble("ALTITUDE", rig.BoresightAltDeg, 3, "Boresight altitude (deg)")
           .SetDouble("AZIMUTH", rig.BoresightAzDeg, 3, "Boresight azimuth (deg)");
  }

  private void StampSiteMetadata(FitsHeaderBuilder builder)
  {
    builder.SetDouble("OBSGEO-LAT", _siteOptions.LatitudeDegrees, 6, "Observatory latitude (deg)")
           .SetDouble("OBSGEO-LON", _siteOptions.LongitudeDegrees, 6, "Observatory longitude (deg)");

    // Note: OBSGEO-ALT (altitude in meters) would go here if we had elevation data
  }

  private static double CalculateMjd(DateTime utc)
  {
    // MJD = JD - 2400000.5
    // JD for epoch J2000.0 (2000-01-01 12:00:00 UTC) = 2451545.0
    var j2000 = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    var daysSinceJ2000 = (utc - j2000).TotalDays;
    var jd = 2451545.0 + daysSinceJ2000;
    return jd - 2400000.5;
  }

  private static double CalculatePixelScale(double pixelSizeMicrons, double focalLengthMm)
  {
    if (pixelSizeMicrons <= 0 || focalLengthMm <= 0)
    {
      return 0;
    }

    // pixel scale (arcsec/pixel) = (pixel_size_mm / focal_length_mm) * 206265
    var pixelSizeMm = pixelSizeMicrons / 1000.0;
    return (pixelSizeMm / focalLengthMm) * 206265.0;
  }
}
