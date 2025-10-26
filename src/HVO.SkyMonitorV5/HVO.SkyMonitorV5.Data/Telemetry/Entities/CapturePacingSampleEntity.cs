using System;
using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.Data.Telemetry.Entities;

public sealed class CapturePacingSampleEntity
{
    [Key]
    public long Id { get; set; }

    [Required]
    public DateTimeOffset CapturedAtUtc { get; set; }

    [Required]
    public DateTimeOffset CapturedAtLocal { get; set; }

    public bool Enabled { get; set; }

    public bool UsingBackgroundStacker { get; set; }

    public int BaseDelayMilliseconds { get; set; }

    public int AdjustedDelayMilliseconds { get; set; }

    public int QueuePressureLevel { get; set; }

    public int PressureAdditionalDelayMilliseconds { get; set; }

    public int PenaltyAdditionalDelayMilliseconds { get; set; }

    public bool PenaltyActive { get; set; }

    public DateTimeOffset? PenaltyExpiresAtLocal { get; set; }
}
