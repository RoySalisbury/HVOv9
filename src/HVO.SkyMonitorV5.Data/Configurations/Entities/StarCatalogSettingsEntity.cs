namespace HVO.SkyMonitorV5.Data.Configurations.Entities;

/// <summary>
/// Represents current runtime star catalog selection and filtering behaviour.
/// </summary>
public sealed class StarCatalogSettingsEntity
{
    public int Id { get; set; }
    public double MagnitudeLimit { get; set; }
    public double MinMaxAltitudeDegrees { get; set; }
    public int TopStarCount { get; set; }
    public bool StratifiedSelection { get; set; }
    public bool IncludePlanets { get; set; }
    public bool IncludeMoon { get; set; }
    public bool IncludeOuterPlanets { get; set; }
    public bool IncludeSun { get; set; }
    public int RightAscensionBins { get; set; }
    public int DeclinationBands { get; set; }
}
