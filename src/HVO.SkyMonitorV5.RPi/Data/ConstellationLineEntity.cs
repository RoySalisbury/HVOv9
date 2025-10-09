using System.Collections.Generic;

namespace HVO.SkyMonitorV5.RPi.Data;

public sealed class ConstellationLineEntity
{
    public int LineId { get; set; }
    public string Constellation { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public int StarCount { get; set; }

    public ICollection<ConstellationLineStarEntity> Stars { get; set; } = new List<ConstellationLineStarEntity>();
}
