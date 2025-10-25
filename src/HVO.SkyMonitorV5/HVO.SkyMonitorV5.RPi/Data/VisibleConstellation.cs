using System;
using System.Collections.Generic;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;

namespace HVO.SkyMonitorV5.RPi.Data;

public sealed class VisibleConstellation
{
    public string ConstellationCode { get; init; } = string.Empty;
    public IReadOnlyList<Star> Stars { get; init; } = Array.Empty<Star>();
}
