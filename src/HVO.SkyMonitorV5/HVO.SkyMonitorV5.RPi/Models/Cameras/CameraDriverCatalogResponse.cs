using System;
using System.Collections.Generic;

namespace HVO.SkyMonitorV5.RPi.Models.Cameras;

public sealed class CameraDriverCatalogResponse
{
    public IReadOnlyList<CameraDriverDescriptorResponse> Drivers { get; init; } = Array.Empty<CameraDriverDescriptorResponse>();
}
