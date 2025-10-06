#nullable enable

using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV4.RPi.HostedServices.AllSkyImageSave;

public sealed class AllSkyImageSaveOptions
{
    [Range(1, int.MaxValue)]
    public uint RestartOnFailureWaitTimeSeconds { get; set; } = 15;

    [Required]
    [MinLength(1)]
    public string ImageSaveRoot { get; set; } = "/home/pi/skymonitor";

    [Range(1, 3600)]
    public int MaxImageAgeSeconds { get; set; } = 60;
}
