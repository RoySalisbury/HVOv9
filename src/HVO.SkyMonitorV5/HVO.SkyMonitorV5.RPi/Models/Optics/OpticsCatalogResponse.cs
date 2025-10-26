using System;
using System.Collections.Generic;

namespace HVO.SkyMonitorV5.RPi.Models.Optics;

public sealed class OpticsCatalogResponse
{
    public IReadOnlyList<OpticsRigSummary> Rigs { get; init; } = Array.Empty<OpticsRigSummary>();
    public IReadOnlyList<OpticsCatalogCamera> Cameras { get; init; } = Array.Empty<OpticsCatalogCamera>();
    public IReadOnlyList<OpticsCatalogOptics> Optics { get; init; } = Array.Empty<OpticsCatalogOptics>();
    public string? ActiveRigKey { get; init; }
}
