using System;
using System.Text.Json.Serialization;
using HVO.SkyMonitorV5.RPi.Exports;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Pipeline;

namespace HVO.SkyMonitorV5.RPi.Models;

public sealed class DiagnosticsFrameExportResponse
{
  public required StageInfo Raw { get; init; }
  public required StageInfo Processed { get; init; }

  public sealed class StageInfo
  {
    public bool Enabled { get; init; }
    public string PayloadScope { get; init; } = string.Empty;
    public EncodingInfo Archive { get; init; } = new();
    public EncodingInfo Delivery { get; init; } = new();
    public bool DeliveryFallbacksToArchive { get; init; }

    public static StageInfo FromStageOptions(FrameExportStageOptions stage, FrameExportStage stageId)
    {
      if (stage is null)
      {
        throw new ArgumentNullException(nameof(stage));
      }

      // Normalize encoding references
      var archive = ImageEncodingUtilities.Normalize(stage.ArchiveEncoding);
      var delivery = stage.DeliveryEncoding is null
          ? archive
          : ImageEncodingUtilities.Normalize(stage.DeliveryEncoding);

      return new StageInfo
      {
        Enabled = stage.Enabled,
        PayloadScope = stage.PayloadScope.ToString(),
        Archive = EncodingInfo.FromEncoding(archive),
        Delivery = EncodingInfo.FromEncoding(delivery),
        DeliveryFallbacksToArchive = stage.DeliveryEncoding is null
      };
    }
  }

  public sealed class EncodingInfo
  {
    public string Format { get; init; } = string.Empty;
    public int Quality { get; init; }
    public string ContentType { get; init; } = "application/octet-stream";
    public string? FileExtension { get; init; }
    public bool IsRaster { get; init; }

    // FITS-only details (present when Format == Fits)
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FitsDetails? Fits { get; init; }

    public static EncodingInfo FromEncoding(ImageEncodingSettings settings)
    {
      var contentType = ImageEncodingUtilities.ToContentType(settings.Format);
      var extension = ImageEncodingUtilities.ToFileExtension(settings.Format);
      var isRaster = ImageEncodingUtilities.IsRasterFormat(settings.Format);

      FitsDetails? fits = null;
      if (settings.Format == ImageEncodingFormat.Fits)
      {
        var fo = settings.FitsOptions ?? new FitsEncodingOptions();
        fits = new FitsDetails
        {
          BitDepth = fo.BitDepth.ToString(),
          ImageFormat = fo.ImageFormat.ToString(),
          Compression = fo.Compression.ToString(),
          UnsignedU16 = fo.UnsignedU16,
          WriteChecksum = fo.WriteChecksum
        };
      }

      return new EncodingInfo
      {
        Format = settings.Format.ToString(),
        Quality = settings.Quality,
        ContentType = contentType,
        FileExtension = extension,
        IsRaster = isRaster,
        Fits = fits
      };
    }

    public sealed class FitsDetails
    {
      public string BitDepth { get; init; } = string.Empty;
      public string ImageFormat { get; init; } = string.Empty;
      public string Compression { get; init; } = string.Empty;
      public bool UnsignedU16 { get; init; }
      public bool WriteChecksum { get; init; }
    }
  }
}
