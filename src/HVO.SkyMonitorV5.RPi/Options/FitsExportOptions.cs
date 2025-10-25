using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.RPi.Options;

public sealed class FitsExportOptions
{
  public const string SectionName = "FitsExport";

  /// <summary>Emit FITS for raw frame exports.</summary>
  public bool EnableForRaw { get; set; } = true;

  /// <summary>Emit FITS for processed frame exports.</summary>
  public bool EnableForProcessed { get; set; } = true;

  /// <summary>Bit depth for FITS images. Default is U16.</summary>
  [Required]
  public FitsBitDepth BitDepth { get; set; } = FitsBitDepth.U16;

  /// <summary>When true and BitDepth=U16, write unsigned scaling (BSCALE=1, BZERO=32768).</summary>
  public bool UnsignedU16 { get; set; } = true;

  /// <summary>Compression algorithm. None by default.</summary>
  [Required]
  public FitsCompressionKind Compression { get; set; } = FitsCompressionKind.None;

  /// <summary>When true, write a FITS checksum.</summary>
  public bool WriteChecksum { get; set; } = true;
}

public enum FitsBitDepth
{
  U8,
  U16
}

public enum FitsCompressionKind
{
  None,
  Rice,
  Gzip1,
  Gzip2,
  HCompress
}
