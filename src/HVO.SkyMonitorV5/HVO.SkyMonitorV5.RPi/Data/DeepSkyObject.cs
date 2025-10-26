using System;

namespace HVO.SkyMonitorV5.RPi.Data;

/// <summary>
/// Represents a deep-sky catalog entry projected on the SkyMonitor dome.
/// </summary>
public sealed record DeepSkyObject(
    string PrimaryId,
    string DisplayName,
    string? Constellation,
    double RightAscensionHours,
    double DeclinationDegrees,
    double? ApparentMagnitude,
    string? ObjectType)
{
    public string FullDisplayName => string.IsNullOrWhiteSpace(DisplayName)
        ? PrimaryId
        : $"{PrimaryId} ({DisplayName})";

    public override string ToString()
    {
        return $"{FullDisplayName} [{Constellation ?? "?"}] RA {RightAscensionHours:F3}h Dec {DeclinationDegrees:F2}° Mag {(ApparentMagnitude?.ToString("F1") ?? "—")}";
    }
}
