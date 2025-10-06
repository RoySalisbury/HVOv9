#nullable enable

using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV4.RPi.HostedServices.AllSkyImageCleanup;

public sealed class AllSkyImageCleanupOptions
{
    [Range(1, int.MaxValue)]
    public uint RestartOnFailureWaitTimeSeconds { get; set; } = 15;

    [Range(1, 168)]
    public int MaxHoursToKeep { get; set; } = 3;
}
