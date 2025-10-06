#nullable enable

using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV4.RPi.HostedServices.AllSkyTimelapse;

public sealed class AllSkyTimelapseOptions
{
    [Range(1, int.MaxValue)]
    public uint RestartOnFailureWaitTimeSeconds { get; set; } = 15;

    [Required]
    [MinLength(1)]
    public string FFMpegPath { get; set; } = "/usr/bin/ffmpeg";

    [Required]
    [MinLength(1)]
    public string FFMpegOutputArgs { get; set; } = "-c:v libx264 -pix_fmt yuv420p -preset fast";

    [Range(1, 240)]
    public int FFMpegVideoFps { get; set; } = 60;

    [Required]
    [MinLength(1)]
    public string ImageSaveRoot { get; set; } = "/home/pi/allSkyImages";

    [Required]
    [MinLength(1)]
    public string OutputPrefix { get; set; } = "AllSkyCam01";
}
