using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.Data.Catalogs.Hyg;

public sealed class HygStar
{
    [Key]
    public int Id { get; set; }

    public int? HipparcosId { get; set; }
    public int? HenryDraperId { get; set; }
    public int? HarvardRevisedId { get; set; }
    public string? GlieseId { get; set; }
    public string? BayerFlamsteed { get; set; }
    public string? ProperName { get; set; }
    public double? RightAscensionHours { get; set; }
    public double? DeclinationDegrees { get; set; }
    public double? DistanceParsecs { get; set; }
    public double? ProperMotionRa { get; set; }
    public double? ProperMotionDec { get; set; }
    public double? RadialVelocity { get; set; }
    public double? Magnitude { get; set; }
    public double? AbsoluteMagnitude { get; set; }
    public string? SpectralType { get; set; }
    public double? ColorIndexBv { get; set; }
    public double? RightAscensionRadians { get; set; }
    public double? DeclinationRadians { get; set; }
    public string? BayerDesignation { get; set; }
    public string? FlamsteedNumber { get; set; }
    public string? Constellation { get; set; }
    public double? Luminosity { get; set; }
    public string? VariableStarDesignation { get; set; }
    public double? VariableMinMagnitude { get; set; }
    public double? VariableMaxMagnitude { get; set; }
}