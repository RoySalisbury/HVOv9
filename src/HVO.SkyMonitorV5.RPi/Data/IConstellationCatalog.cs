using System.Collections.Generic;

namespace HVO.SkyMonitorV5.RPi.Data;

public interface IConstellationCatalog
{
    IReadOnlyDictionary<string, IReadOnlyList<ConstellationStar>> GetAll();
}
