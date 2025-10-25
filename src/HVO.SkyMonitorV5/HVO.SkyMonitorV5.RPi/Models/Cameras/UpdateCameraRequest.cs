using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.RPi.Models.Cameras;

public sealed class UpdateCameraRequest : CreateCameraRequest
{
    [Required]
    [Range(1, long.MaxValue)]
    public long Revision { get; set; }
}
