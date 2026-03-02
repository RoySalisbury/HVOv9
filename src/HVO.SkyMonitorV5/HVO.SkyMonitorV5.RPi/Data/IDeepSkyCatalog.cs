using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HVO.Core.Results;

namespace HVO.SkyMonitorV5.RPi.Data;

public interface IDeepSkyCatalog
{
    Task<Result<IReadOnlyList<DeepSkyObject>>> GetDeepSkyObjectsAsync(
        double magnitudeLimit = 8.0,
        int limit = 50,
        string? objectType = null,
        CancellationToken cancellationToken = default);
}
