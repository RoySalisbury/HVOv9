using System.Collections.Generic;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;

namespace HVO.SkyMonitorV5.RPi.Data;

public interface IConstellationCatalog
{
    IReadOnlyList<ConstellationFigure> GetFigures();

    IReadOnlyDictionary<string, IReadOnlyList<Star>> GetStarLookup();
}
