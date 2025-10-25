using System.Collections.Generic;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;

namespace HVO.SkyMonitorV5.RPi.Data;

public sealed record ConstellationFigure(string Abbreviation, string DisplayName, IReadOnlyList<ConstellationFigureLine> Lines);

public sealed record ConstellationFigureLine(int LineNumber, IReadOnlyList<Star> Stars);
