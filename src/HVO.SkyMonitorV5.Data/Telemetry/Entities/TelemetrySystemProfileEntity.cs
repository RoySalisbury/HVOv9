using System;

namespace HVO.SkyMonitorV5.Data.Telemetry.Entities;

public sealed class TelemetrySystemProfileEntity
{
    public long Id { get; set; }

    public string SystemHash { get; set; } = string.Empty;

    public string? MachineName { get; set; }

    public string? HostName { get; set; }

    public string? OperatingSystem { get; set; }

    public string? OsArchitecture { get; set; }

    public string? ProcessArchitecture { get; set; }

    public string? FrameworkDescription { get; set; }

    public int? ProcessorCount { get; set; }

    public double? TotalMemoryMegabytes { get; set; }

    public string? CpuModel { get; set; }

    public string? HardwareModel { get; set; }

    public bool? IsContainerized { get; set; }

    public string? AdditionalPropertiesJson { get; set; }

    public DateTimeOffset FirstSeenAtUtc { get; set; }

    public DateTimeOffset LastSeenAtUtc { get; set; }
}
