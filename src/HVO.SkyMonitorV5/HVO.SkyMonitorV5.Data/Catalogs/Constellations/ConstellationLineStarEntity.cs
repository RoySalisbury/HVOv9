namespace HVO.SkyMonitorV5.Data.Catalogs.Constellations;

public sealed class ConstellationLineStarEntity
{
    public int LineId { get; set; }
    public int SequenceIndex { get; set; }
    public int BscNumber { get; set; }

    public ConstellationLineEntity Line { get; set; } = null!;
}