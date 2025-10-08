using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.RPi.Models;

public sealed class UpdateCameraConfigurationRequest
{
    public bool? EnableStacking { get; set; }

    [Range(1, 32)]
    public int? StackingFrameCount { get; set; }

    [Range(1, 240)]
    public int? StackingBufferMinimumFrames { get; set; }

    [Range(0, 3_600)]
    public int? StackingBufferIntegrationSeconds { get; set; }

    public bool? EnableImageOverlays { get; set; }

    public bool? EnableCircularApertureMask { get; set; }

    public List<string>? FrameFilters { get; set; }
}
