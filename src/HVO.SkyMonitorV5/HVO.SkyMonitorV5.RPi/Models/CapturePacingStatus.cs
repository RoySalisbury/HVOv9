using System;

namespace HVO.SkyMonitorV5.RPi.Models;

/// <summary>
/// Reflects the current capture pacing state derived from queue pressure and rejection penalties.
/// </summary>
public sealed record CapturePacingStatus(
    DateTimeOffset Timestamp,
    bool Enabled,
    bool UsingBackgroundStacker,
    int BaseDelayMilliseconds,
    int AdjustedDelayMilliseconds,
    int QueuePressureLevel,
    int PressureAdditionalDelayMilliseconds,
    int PenaltyAdditionalDelayMilliseconds,
    bool PenaltyActive,
    DateTimeOffset? PenaltyExpiresAt);
