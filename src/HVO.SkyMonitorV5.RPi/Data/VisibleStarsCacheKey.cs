using System;
using System.Collections.Generic;
using HVO.SkyMonitorV5.RPi.Cameras.MockCamera;
using Microsoft.Extensions.Caching.Memory;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Data;

/*
We bucket utc to the hour—great for starfields where minute-by-minute changes are visually negligible but you still want time awareness. If you need finer granularity, change to DateTime.Utc truncated to 10–15 minutes.
*/
public readonly record struct VisibleStarsCacheKey(
    double LatitudeDeg,
    double LongitudeDeg,
    DateTime UtcHour,      // hour-bucketed time to avoid per-minute churn
    double MagnitudeLimit,
    double MinMaxAltitudeDeg,
    int TopN,
    bool Stratified,
    int RaBins,
    int DecBands,
    int ScreenWidth,
    int ScreenHeight
);


public sealed class CachedStarRepository : IStarRepository
{
    private readonly IStarRepository _inner;
    private readonly IMemoryCache _cache;
    private readonly MemoryCacheEntryOptions _defaultOptions;

    public CachedStarRepository(IStarRepository inner, IMemoryCache cache,
                                TimeSpan? absoluteTtl = null, TimeSpan? slidingTtl = null)
    {
        _inner = inner;
        _cache = cache;
        _defaultOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = absoluteTtl ?? TimeSpan.FromMinutes(30),
            SlidingExpiration = slidingTtl ?? TimeSpan.FromMinutes(10),
            Size = 1   // lets you set overall cache size limit if you turn it on
        };
    }

    // ---- Visible stars (heavy query + projection) ----
    public async Task<List<Star>> GetVisibleStarsAsync(
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
        // Bucket to the hour
        var utcHour = new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc);
        var key = new VisibleStarsCacheKey(latitudeDeg, longitudeDeg, utcHour, magnitudeLimit,
                                           minMaxAltitudeDeg, topN, stratified, raBins, decBands,
                                           screenWidth, screenHeight);

        if (_cache.TryGetValue(key, out List<Star>? cached) && cached is not null)
            return cached;

        var fresh = await _inner.GetVisibleStarsAsync(latitudeDeg, longitudeDeg, utc,
                                                      magnitudeLimit, minMaxAltitudeDeg,
                                                      topN, stratified, raBins, decBands,
                                                      screenWidth, screenHeight);
        _cache.Set(key, fresh, _defaultOptions);
        return fresh;
    }

    // ---- Pass-throughs (you can cache these too if you like) ----
    public Task<List<Star>> GetConstellationStarsAsync(string constellation3, double magnitudeLimit = 6.0)
        => _inner.GetConstellationStarsAsync(constellation3, magnitudeLimit);

    public Task<List<HygStar>> SearchByNameAsync(string query, int limit = 20)
        => _inner.SearchByNameAsync(query, limit);

    public Task<List<Star>> GetRaWindowAsync(double raStartHours, double raEndHours, double magnitudeLimit = 6.0)
        => _inner.GetRaWindowAsync(raStartHours, raEndHours, magnitudeLimit);

    public Task<List<Star>> GetBrightestEverHighAsync(double latitudeDeg, double minMaxAltitudeDeg = 10.0,
                                                      int topN = 200, double magnitudeLimit = 6.5)
        => _inner.GetBrightestEverHighAsync(latitudeDeg, minMaxAltitudeDeg, topN, magnitudeLimit);

    public Task<IReadOnlyDictionary<string, IReadOnlyList<ConstellationStar>>> GetConstellationsAsync()
        => _inner.GetConstellationsAsync();

        public Task<List<VisibleConstellation>> GetVisibleByConstellationAsync(
            double latitudeDeg,
            double longitudeDeg,
            DateTime utc,
            double magnitudeLimit = 6.5,
            double minMaxAltitudeDeg = 10.0,
            int screenWidth = 1,
            int screenHeight = 1)
            => _inner.GetVisibleByConstellationAsync(latitudeDeg, longitudeDeg, utc, magnitudeLimit, minMaxAltitudeDeg, screenWidth, screenHeight);

    // ---- Optional helpers ----
    public void InvalidateVisibleCacheForLocation(double latitudeDeg, double longitudeDeg)
    {
        // If you need targeted invalidation, keep your own key registry.
        // Simpler option: clear the entire memory cache from outside via IMemoryCache Dispose.
    }
}

