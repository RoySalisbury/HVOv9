#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.RPi.Cameras.MockCamera;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVO.SkyMonitorV5.RPi.Data;

public sealed class CachedPlanetRepository : IPlanetRepository
{
    private readonly IPlanetRepository _inner;
    private readonly IMemoryCache _cache;
    private readonly MemoryCacheEntryOptions _defaultOptions;
    private readonly ILogger<CachedPlanetRepository> _logger;

    public CachedPlanetRepository(
        IPlanetRepository inner,
        IMemoryCache cache,
        TimeSpan? absoluteTtl = null,
        TimeSpan? slidingTtl = null,
        ILogger<CachedPlanetRepository>? logger = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? NullLogger<CachedPlanetRepository>.Instance;
        _defaultOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = absoluteTtl ?? TimeSpan.FromMinutes(15),
            SlidingExpiration = slidingTtl ?? TimeSpan.FromMinutes(5),
            Size = 1
        };
    }

    public async Task<Result<IReadOnlyList<PlanetMark>>> GetVisiblePlanetsAsync(
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

        var utcHour = new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc);
        var allowedBodiesToken = VisiblePlanetsCacheKey.CreateAllowedBodiesToken(criteria.AllowedBodies);
        var key = new VisiblePlanetsCacheKey(
            latitudeDeg,
            longitudeDeg,
            utcHour,
            criteria.IncludePlanets,
            criteria.IncludeMoon,
            criteria.IncludeOuterPlanets,
            criteria.IncludeSun,
            allowedBodiesToken);

        if (_cache.TryGetValue(key, out IReadOnlyList<PlanetMark>? cached) && cached is not null)
        {
            _logger.LogTrace(
                "Planet cache hit for {LatitudeDeg}, {LongitudeDeg} at {UtcHour}.",
                latitudeDeg,
                longitudeDeg,
                utcHour);
            return Result<IReadOnlyList<PlanetMark>>.Success(cached);
        }

        var result = await _inner.GetVisiblePlanetsAsync(latitudeDeg, longitudeDeg, utc, criteria, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccessful)
        {
            var value = result.Value;
            _cache.Set(key, value, _defaultOptions);
            _logger.LogDebug(
                "Cached planet marks for {LatitudeDeg}, {LongitudeDeg} at {UtcHour}; count={Count} bodies.",
                latitudeDeg,
                longitudeDeg,
                utcHour,
                value.Count);
        }
        else if (result.Error is not null)
        {
            _logger.LogError(result.Error, "Failed to compute planet marks for caching at {LatitudeDeg}, {LongitudeDeg}.", latitudeDeg, longitudeDeg);
        }

        return result;
    }
}
