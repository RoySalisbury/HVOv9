using System;
using System.Collections.Generic;

namespace HVO.SkyMonitorV5.RPi.Models;

public sealed record CoreCpuLoad(string Name, double UsagePercent);

public sealed record MemoryUsageSnapshot(
    double? TotalMegabytes,
    double? UsedMegabytes,
    double? FreeMegabytes,
    double? AvailableMegabytes,
    double? CachedMegabytes,
    double? BuffersMegabytes,
    double? UsagePercent);

public sealed record SystemDiagnosticsSnapshot(
    double? TotalCpuPercent,
    double ProcessCpuPercent,
    IReadOnlyList<CoreCpuLoad> CoreCpuLoads,
    MemoryUsageSnapshot Memory,
    double ProcessWorkingSetMegabytes,
    double ProcessPrivateMegabytes,
    double ManagedMemoryMegabytes,
    int ThreadCount,
    double UptimeSeconds,
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset GeneratedAtLocal);
