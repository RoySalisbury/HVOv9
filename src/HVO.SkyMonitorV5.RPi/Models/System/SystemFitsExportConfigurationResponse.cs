using System.ComponentModel.DataAnnotations;
using HVO.SkyMonitorV5.RPi.Options;

namespace HVO.SkyMonitorV5.RPi.Models.System;

public sealed class SystemFitsExportConfigurationResponse
{
  public required bool EnableForRaw { get; init; }

  public required bool EnableForProcessed { get; init; }

  public required FitsBitDepth BitDepth { get; init; }

  public required bool UnsignedU16 { get; init; }

  public required FitsCompressionKind Compression { get; init; }

  public required bool WriteChecksum { get; init; }

  public required long Revision { get; init; }
}

public sealed class UpdateSystemFitsExportRequest
{
  [Range(0, long.MaxValue)]
  public long Revision { get; set; }

  public bool EnableForRaw { get; set; } = true;

  public bool EnableForProcessed { get; set; } = true;

  [Required]
  public FitsBitDepth BitDepth { get; set; } = FitsBitDepth.U16;

  public bool UnsignedU16 { get; set; } = true;

  [Required]
  public FitsCompressionKind Compression { get; set; } = FitsCompressionKind.None;

  public bool WriteChecksum { get; set; } = true;
}
