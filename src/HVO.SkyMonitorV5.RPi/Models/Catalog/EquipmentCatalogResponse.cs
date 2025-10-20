using System;
using System.Collections.Generic;
using HVO.SkyMonitorV5.RPi.Models.Adapters;
using HVO.SkyMonitorV5.RPi.Models.Cameras;
using HVO.SkyMonitorV5.RPi.Models.Optics;
using HVO.SkyMonitorV5.RPi.Models.Rigs;

namespace HVO.SkyMonitorV5.RPi.Models.Catalog;

public sealed class EquipmentCatalogResponse
{
    public IReadOnlyList<RigSummary> Rigs { get; init; } = Array.Empty<RigSummary>();
    public IReadOnlyList<CameraCatalogItem> Cameras { get; init; } = Array.Empty<CameraCatalogItem>();
    public IReadOnlyList<OpticsCatalogItem> Optics { get; init; } = Array.Empty<OpticsCatalogItem>();
    public IReadOnlyList<AdapterSummary> Adapters { get; init; } = Array.Empty<AdapterSummary>();
    public string? ActiveRigKey { get; init; }
}
