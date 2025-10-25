#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;

namespace HVO.SkyMonitorV5.RPi.Data;

public interface IPlanetRepository
{
    Task<Result<IReadOnlyList<PlanetMark>>> GetVisiblePlanetsAsync(
        double latitudeDeg,
        double longitudeDeg,
        DateTime utc,
        PlanetVisibilityCriteria criteria,
        CancellationToken cancellationToken = default);
}
