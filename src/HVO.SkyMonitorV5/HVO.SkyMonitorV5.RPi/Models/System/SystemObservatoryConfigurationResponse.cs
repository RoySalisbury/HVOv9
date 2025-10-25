using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.RPi.Models.System;

public sealed class SystemObservatoryConfigurationResponse
{
    public required int Id { get; init; }

    public required long Revision { get; init; }

    public required string Slug { get; init; }

    public required string Name { get; init; }

    public required double LatitudeDegrees { get; init; }

    public required double LongitudeDegrees { get; init; }

    public required string TimeZoneId { get; init; }
}

public sealed class UpdateSystemObservatoryRequest
{
    [Range(1, int.MaxValue)]
    public int Id { get; set; }

    [Range(0, long.MaxValue)]
    public long Revision { get; set; }

    [Required]
    [StringLength(64, MinimumLength = 2)]
    public string Slug { get; set; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Range(-90, 90)]
    public double LatitudeDegrees { get; set; }
        = 35.347;

    [Range(-180, 180)]
    public double LongitudeDegrees { get; set; }
        = -113.878;

    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string TimeZoneId { get; set; } = "UTC";
}
