using System;
using System.Collections.Generic;

namespace HVO.SkyMonitorV5.RPi.Models.Optics;

public sealed class OpticsCatalogResponse
{
    public IReadOnlyList<OpticsRigSummary> Rigs { get; init; } = Array.Empty<OpticsRigSummary>();
    public IReadOnlyList<OpticsCatalogCamera> Cameras { get; init; } = Array.Empty<OpticsCatalogCamera>();
    public IReadOnlyList<OpticsCatalogLens> Lenses { get; init; } = Array.Empty<OpticsCatalogLens>();
    public string? ActiveRigKey { get; init; }
}
