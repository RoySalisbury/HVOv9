using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.RPi.Cameras.MockCamera;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVO.SkyMonitorV5.RPi.Data;

public sealed class CachedStarRepository : IStarRepository
{
    private readonly IStarRepository _inner;
    private readonly IMemoryCache _cache;
    private readonly MemoryCacheEntryOptions _defaultOptions;
    private readonly ILogger<CachedStarRepository> _logger;

    public CachedStarRepository(
        IStarRepository inner,
        IMemoryCache cache,
        TimeSpan? absoluteTtl = null,
        TimeSpan? slidingTtl = null,
        ILogger<CachedStarRepository>? logger = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? NullLogger<CachedStarRepository>.Instance;
        _defaultOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = absoluteTtl ?? TimeSpan.FromMinutes(30),
            SlidingExpiration = slidingTtl ?? TimeSpan.FromMinutes(10),
            Size = 1
        };
    }

    public async Task<Result<IReadOnlyList<Star>>> GetVisibleStarsAsync(
        double latitudeDeg,
        double longitudeDeg,
        DateTime utc,
        double magnitudeLimit = 6.5,
        double minMaxAltitudeDeg = 10.0,
        int topN = 300,
        bool stratified = false,
        int raBins = 24,
        int decBands = 8,
        int screenWidth = 1,
        int screenHeight = 1)
    {
        var utcHour = new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc);
        var key = new VisibleStarsCacheKey(latitudeDeg, longitudeDeg, utcHour, magnitudeLimit,
            minMaxAltitudeDeg, topN, stratified, raBins, decBands, screenWidth, screenHeight);

        if (_cache.TryGetValue(key, out IReadOnlyList<Star>? cached) && cached is not null)
        {
            _logger.LogTrace("Visible stars cache hit for {LatitudeDeg}, {LongitudeDeg} at {UtcHour}.", latitudeDeg, longitudeDeg, utcHour);
            return Result<IReadOnlyList<Star>>.Success(cached);
        }

        var result = await _inner.GetVisibleStarsAsync(latitudeDeg, longitudeDeg, utc, magnitudeLimit, minMaxAltitudeDeg,
            topN, stratified, raBins, decBands, screenWidth, screenHeight).ConfigureAwait(false);

        if (result.IsSuccessful)
        {
            var value = result.Value;
            _cache.Set(key, value, _defaultOptions);
            _logger.LogDebug("Cached visible stars for {LatitudeDeg}, {LongitudeDeg} at {UtcHour}; count={Count}.",
                latitudeDeg, longitudeDeg, utcHour, value.Count);
        }
        else if (result.Error is not null)
        {
            _logger.LogError(result.Error, "Failed to load visible stars for caching at {LatitudeDeg}, {LongitudeDeg}.",
                latitudeDeg, longitudeDeg);
        }

        return result;
    }

    public Task<Result<IReadOnlyList<Star>>> GetConstellationStarsAsync(string constellation3, double magnitudeLimit = 6.0)
        => _inner.GetConstellationStarsAsync(constellation3, magnitudeLimit);

    public Task<Result<IReadOnlyList<HygStar>>> SearchByNameAsync(string query, int limit = 20)
        => _inner.SearchByNameAsync(query, limit);

    public Task<Result<IReadOnlyList<Star>>> GetRaWindowAsync(double raStartHours, double raEndHours, double magnitudeLimit = 6.0)
        => _inner.GetRaWindowAsync(raStartHours, raEndHours, magnitudeLimit);

    public Task<Result<IReadOnlyList<Star>>> GetBrightestEverHighAsync(double latitudeDeg, double minMaxAltitudeDeg = 10.0,
        int topN = 200, double magnitudeLimit = 6.5)
        => _inner.GetBrightestEverHighAsync(latitudeDeg, minMaxAltitudeDeg, topN, magnitudeLimit);

    public Task<Result<IReadOnlyDictionary<string, IReadOnlyList<ConstellationStar>>>> GetConstellationsAsync()
        => _inner.GetConstellationsAsync();

    public Task<Result<IReadOnlyList<VisibleConstellation>>> GetVisibleByConstellationAsync(
        double latitudeDeg,
        double longitudeDeg,
        DateTime utc,
        double magnitudeLimit = 6.5,
        double minMaxAltitudeDeg = 10.0,
        int screenWidth = 1,
        int screenHeight = 1)
        => _inner.GetVisibleByConstellationAsync(latitudeDeg, longitudeDeg, utc, magnitudeLimit, minMaxAltitudeDeg, screenWidth, screenHeight);

    public void InvalidateVisibleCacheForLocation(double latitudeDeg, double longitudeDeg)
    {
        // Implement key tracking if targeted invalidation is required.
    }
}
