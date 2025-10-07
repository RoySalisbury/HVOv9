#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVO.SkyMonitorV5.RPi.Data;

public sealed class PlanetRepository : IPlanetRepository
{
    private readonly ILogger<PlanetRepository> _logger;

    public PlanetRepository(ILogger<PlanetRepository>? logger = null)
    {
        _logger = logger ?? NullLogger<PlanetRepository>.Instance;
    }

    public Task<Result<IReadOnlyList<PlanetMark>>> GetVisiblePlanetsAsync(
        double latitudeDeg,
        double longitudeDeg,
        DateTime utc,
        PlanetVisibilityCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        if (criteria is null)
        {
            throw new ArgumentNullException(nameof(criteria));
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!criteria.ShouldCompute)
            {
                _logger.LogTrace("Planet visibility request skipped: no planet bodies enabled.");
                return Task.FromResult(Result<IReadOnlyList<PlanetMark>>.Success(Array.Empty<PlanetMark>()));
            }

            var bodies = criteria.ResolveBodies();
            if (bodies.Count == 0)
            {
                _logger.LogDebug("Planet visibility request resolved zero planet bodies for criteria.");
                return Task.FromResult(Result<IReadOnlyList<PlanetMark>>.Success(Array.Empty<PlanetMark>()));
            }

            var marks = PlanetMarks.Compute(latitudeDeg, longitudeDeg, utc, bodies);
            _logger.LogTrace(
                "Computed {PlanetCount} planet mark(s) for {LatitudeDeg}, {LongitudeDeg} at {Utc}.",
                marks.Count,
                latitudeDeg,
                longitudeDeg,
                utc);

            return Task.FromResult(Result<IReadOnlyList<PlanetMark>>.Success(marks));
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogDebug(ex, "Planet visibility request cancelled.");
            return Task.FromResult(Result<IReadOnlyList<PlanetMark>>.Failure(ex));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compute planet marks for {LatitudeDeg}, {LongitudeDeg} at {Utc}.", latitudeDeg, longitudeDeg, utc);
            return Task.FromResult(Result<IReadOnlyList<PlanetMark>>.Failure(ex));
        }
    }
}
