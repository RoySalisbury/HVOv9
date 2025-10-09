using System;
using System.ComponentModel.DataAnnotations;
using HVO.SkyMonitorV5.RPi.Pipeline;

namespace HVO.SkyMonitorV5.RPi.Options;

/// <summary>
/// Configuration options governing processed image encoding.
/// </summary>
public sealed class ImageEncodingOptions
{
    public ImageEncodingFormat Format { get; set; } = ImageEncodingFormat.Jpeg;

    [Range(1, 100)]
    public int Quality { get; set; } = 90;

    public ImageEncodingSettings ToSettings()
        => new(Format, Math.Clamp(Quality, 1, 100));
}
