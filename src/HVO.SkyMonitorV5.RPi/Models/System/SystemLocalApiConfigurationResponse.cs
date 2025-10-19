using System;
using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.RPi.Models.System;

public sealed class SystemLocalApiConfigurationResponse
{
    public string? BaseAddress { get; init; }

    public string? ApiKey { get; init; }

    public required string ApiKeyHeaderName { get; init; }

    public required TimeSpan Timeout { get; init; }

    public required long Revision { get; init; }
}

public sealed class UpdateSystemLocalApiRequest
{
    [Range(0, long.MaxValue)]
    public long Revision { get; set; }

    [StringLength(256)]
    public string? BaseAddress { get; set; }
        = string.Empty;

    [StringLength(512)]
    public string? ApiKey { get; set; }
        = string.Empty;

    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string ApiKeyHeaderName { get; set; } = "X-Api-Key";

    [Range(typeof(double), "0.1", "600", ConvertValueInInvariantCulture = true)]
    public double TimeoutSeconds { get; set; } = 10d;
}
