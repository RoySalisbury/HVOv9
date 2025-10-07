using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.RPi.Options;

/// <summary>
/// Represents a single configurable frame filter entry for the capture pipeline.
/// </summary>
public sealed class FrameFilterOption
{
    [Required]
    [MinLength(1)]
    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public int Order { get; set; }
}
