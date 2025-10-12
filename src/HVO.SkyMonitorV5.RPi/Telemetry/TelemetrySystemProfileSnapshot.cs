using System;

namespace HVO.SkyMonitorV5.RPi.Telemetry;

internal sealed record TelemetrySystemProfileSnapshot(
    string SystemHash,
    string? MachineName,
    string? HostName,
    string? OperatingSystem,
    string? OsArchitecture,
    string? ProcessArchitecture,
    string? FrameworkDescription,
    int? ProcessorCount,
    double? TotalMemoryMegabytes,
    string? CpuModel,
    string? HardwareModel,
    bool? IsContainerized,
    string? AdditionalPropertiesJson,
    DateTimeOffset FirstSeenAtUtc,
    DateTimeOffset LastSeenAtUtc);
