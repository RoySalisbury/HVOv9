#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.RPi.Cameras;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;
using HVO.SkyMonitorV5.RPi.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.SkyMonitorV5.RPi.Data;

public sealed class SkyMonitorRepository : IStarRepository, IPlanetRepository, IConstellationCatalog
{
    private static readonly IReadOnlyDictionary<string, string> ConstellationNameLookup = ConstellationNames.BuildDisplayNameLookup();
    private static readonly TimeSpan VisibleStarsAbsoluteTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan VisibleStarsSlidingTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ConstellationStarsAbsoluteTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan SearchAbsoluteTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan WindowAbsoluteTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan BrightestAbsoluteTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan VisibleConstellationAbsoluteTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan VisiblePlanetsAbsoluteTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan VisiblePlanetsSlidingTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ConstellationCatalogAbsoluteTtl = TimeSpan.FromHours(12);
    private static readonly TimeSpan ConstellationCatalogSlidingTtl = TimeSpan.FromHours(1);

    private static readonly IReadOnlyDictionary<int, HarvardAlias> HarvardRevisedAliasMap = new Dictionary<int, HarvardAlias>
    {
        [596] = new HarvardAlias(595, 9487, "Alpha Psc components share HIP 9487; HYG keeps HR 595"),
        [888] = new HarvardAlias(887, 13914, "Epsilon Ari components share HIP 13914; HYG keeps HR 887"),
        [898] = new HarvardAlias(897, 13847, "Theta Eri pair; HYG keeps HR 897"),
        [1190] = new HarvardAlias(1189, 17797, "f Eri pair is one Hipparcos source; HYG keeps HR 1189"),
        [4058] = new HarvardAlias(4057, 50583, "Gamma Leo A/B; HYG keeps HR 4057"),
        [5506] = new HarvardAlias(5505, 72105, "Epsilon Boo (Izar); HYG keeps HR 5505"),
        [3206] = new HarvardAlias(3207, 39953, "Gamma Vel pair; HYG keeps HR 3207")
    };

    private static readonly MemoryCacheEntryOptions VisibleStarsCacheOptions = CreateCacheEntryOptions(VisibleStarsAbsoluteTtl, VisibleStarsSlidingTtl);
    private static readonly MemoryCacheEntryOptions ConstellationStarsCacheOptions = CreateCacheEntryOptions(ConstellationStarsAbsoluteTtl, TimeSpan.FromMinutes(20));
    private static readonly MemoryCacheEntryOptions SearchCacheOptions = CreateCacheEntryOptions(SearchAbsoluteTtl, TimeSpan.FromMinutes(5));
    private static readonly MemoryCacheEntryOptions WindowCacheOptions = CreateCacheEntryOptions(WindowAbsoluteTtl, TimeSpan.FromMinutes(3));
    private static readonly MemoryCacheEntryOptions BrightestCacheOptions = CreateCacheEntryOptions(BrightestAbsoluteTtl, TimeSpan.FromMinutes(5));
    private static readonly MemoryCacheEntryOptions VisibleConstellationCacheOptions = CreateCacheEntryOptions(VisibleConstellationAbsoluteTtl, TimeSpan.FromMinutes(5));
    private static readonly MemoryCacheEntryOptions VisiblePlanetsCacheOptions = CreateCacheEntryOptions(VisiblePlanetsAbsoluteTtl, VisiblePlanetsSlidingTtl);
    private static readonly MemoryCacheEntryOptions ConstellationCatalogCacheOptions = CreateCacheEntryOptions(ConstellationCatalogAbsoluteTtl, ConstellationCatalogSlidingTtl);

    private static readonly IReadOnlyList<Star> EmptyStarList = Array.AsReadOnly(Array.Empty<Star>());
    private static readonly IReadOnlyList<VisibleConstellation> EmptyVisibleConstellations = Array.AsReadOnly(Array.Empty<VisibleConstellation>());
    private static readonly IReadOnlyList<PlanetMark> EmptyPlanetMarks = Array.AsReadOnly(Array.Empty<PlanetMark>());

    private readonly HygContext _hygContext;
    private readonly IDbContextFactory<ConstellationCatalogContext> _constellationContextFactory;
    private readonly IMemoryCache _cache;
    private readonly IOptionsMonitor<StarCatalogOptions> _catalogOptions;
    private readonly ILogger<SkyMonitorRepository> _logger;

    private readonly object _constellationCacheLock = new();

    public SkyMonitorRepository(
        HygContext hygContext,
        IDbContextFactory<ConstellationCatalogContext> constellationContextFactory,
        IMemoryCache cache,
        IOptionsMonitor<StarCatalogOptions> catalogOptions,
        ILogger<SkyMonitorRepository> logger)
    {
        _hygContext = hygContext ?? throw new ArgumentNullException(nameof(hygContext));
        _constellationContextFactory = constellationContextFactory ?? throw new ArgumentNullException(nameof(constellationContextFactory));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _catalogOptions = catalogOptions ?? throw new ArgumentNullException(nameof(catalogOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
        int screenHeight = 1,
        StarFieldEngine? engine = null)
    {
        try
        {
            if (engine is null)
            {
                var key = CacheKeys.Build(
                    "VisibleStars",
                    RoundUtcToHour(utc),
                    latitudeDeg,
                    longitudeDeg,
                    magnitudeLimit,
                    minMaxAltitudeDeg,
                    topN,
                    stratified,
                    raBins,
                    decBands,
                    screenWidth,
                    screenHeight);

                if (_cache.TryGetValue(key, out IReadOnlyList<Star>? cached) && cached is not null)
                {
                    _logger.LogTrace(
                        "Visible stars cache hit for {LatitudeDeg}, {LongitudeDeg} at {UtcHour}.",
                        latitudeDeg,
                        longitudeDeg,
                        RoundUtcToHour(utc));
                    return Result<IReadOnlyList<Star>>.Success(cached);
                }

                var computed = await ComputeVisibleStarsAsync(
                    latitudeDeg,
                    longitudeDeg,
                    utc,
                    magnitudeLimit,
                    minMaxAltitudeDeg,
                    topN,
                    stratified,
                    raBins,
                    decBands,
                    screenWidth,
                    screenHeight,
                    engine: null).ConfigureAwait(false);

                if (computed.IsSuccessful)
                {
                    var value = computed.Value;
                    _cache.Set(key, value, VisibleStarsCacheOptions);
                }

                return computed;
            }

            return await ComputeVisibleStarsAsync(
                latitudeDeg,
                longitudeDeg,
                utc,
                magnitudeLimit,
                minMaxAltitudeDeg,
                topN,
                stratified,
                raBins,
                decBands,
                screenWidth,
                screenHeight,
                engine).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compute visible stars for {LatitudeDeg}, {LongitudeDeg} at {Utc}.", latitudeDeg, longitudeDeg, utc);
            return Result<IReadOnlyList<Star>>.Failure(ex);
        }
    }

    public async Task<Result<IReadOnlyList<Star>>> GetConstellationStarsAsync(string constellation3, double magnitudeLimit = 6.0)
    {
        try
        {
            var code = (constellation3 ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(code))
            {
                return Result<IReadOnlyList<Star>>.Success(EmptyStarList);
            }

            var key = CacheKeys.Build("ConstellationStars", code.ToUpperInvariant(), magnitudeLimit);
            if (_cache.TryGetValue(key, out IReadOnlyList<Star>? cached) && cached is not null)
            {
                return Result<IReadOnlyList<Star>>.Success(cached);
            }

            var rows = await _hygContext.Stars
                .Where(s => s.Constellation == code && s.Magnitude != null && s.Magnitude <= magnitudeLimit
                            && s.RightAscensionHours != null && s.DeclinationDegrees != null)
                .OrderBy(s => s.Magnitude)
                .ToListAsync()
                .ConfigureAwait(false);

            if (rows.Count == 0)
            {
                _logger.LogDebug("Constellation {ConstellationCode} produced no stars under magnitude {MagnitudeLimit}.", code, magnitudeLimit);
                return Result<IReadOnlyList<Star>>.Success(EmptyStarList);
            }

            var stars = rows
                .Select(star => MapToStar(star))
                .ToArray();
            var resultList = Array.AsReadOnly(stars);

            _cache.Set(key, resultList, ConstellationStarsCacheOptions);
            return Result<IReadOnlyList<Star>>.Success(resultList);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query constellation stars for {Constellation}.", constellation3);
            return Result<IReadOnlyList<Star>>.Failure(ex);
        }
    }

    public async Task<Result<IReadOnlyList<HygStar>>> SearchByNameAsync(string query, int limit = 20)
    {
        try
        {
            var term = (query ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(term))
            {
                return Result<IReadOnlyList<HygStar>>.Success(Array.Empty<HygStar>());
            }

            var key = CacheKeys.Build("Search", term.ToUpperInvariant(), limit);
            if (_cache.TryGetValue(key, out IReadOnlyList<HygStar>? cached) && cached is not null)
            {
                return Result<IReadOnlyList<HygStar>>.Success(cached);
            }

            var matches = await _hygContext.Stars
                .Where(s =>
                    (s.ProperName != null && EF.Functions.Like(s.ProperName, $"%{term}%")) ||
                    (s.BayerFlamsteed != null && EF.Functions.Like(s.BayerFlamsteed, $"%{term}%")))
                .OrderBy(s => s.Magnitude)
                .Take(limit)
                .ToListAsync()
                .ConfigureAwait(false);

            var result = matches.Count == 0
                ? Array.AsReadOnly(Array.Empty<HygStar>())
                : Array.AsReadOnly(matches.ToArray());

            _cache.Set(key, result, SearchCacheOptions);
            return Result<IReadOnlyList<HygStar>>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search stars by name for query '{Query}'.", query);
            return Result<IReadOnlyList<HygStar>>.Failure(ex);
        }
    }

    public async Task<Result<IReadOnlyList<Star>>> GetRaWindowAsync(double raStartHours, double raEndHours, double magnitudeLimit = 6.0)
    {
        try
        {
            var key = CacheKeys.Build("RaWindow", raStartHours, raEndHours, magnitudeLimit);
            if (_cache.TryGetValue(key, out IReadOnlyList<Star>? cached) && cached is not null)
            {
                return Result<IReadOnlyList<Star>>.Success(cached);
            }

            var query = _hygContext.Stars.Where(s => s.Magnitude != null && s.Magnitude <= magnitudeLimit
                                                     && s.RightAscensionHours != null && s.DeclinationDegrees != null);

            if (raEndHours >= raStartHours)
            {
                query = query.Where(s => s.RightAscensionHours! >= raStartHours && s.RightAscensionHours! < raEndHours);
            }
            else
            {
                query = query.Where(s => s.RightAscensionHours! >= raStartHours || s.RightAscensionHours! < raEndHours);
            }

            var rows = await query.OrderBy(s => s.DeclinationDegrees).ToListAsync().ConfigureAwait(false);
            if (rows.Count == 0)
            {
                return Result<IReadOnlyList<Star>>.Success(EmptyStarList);
            }

            var stars = rows.Select(star => MapToStar(star)).ToArray();
            var resultList = Array.AsReadOnly(stars);

            _cache.Set(key, resultList, WindowCacheOptions);
            return Result<IReadOnlyList<Star>>.Success(resultList);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query RA window {Start}–{End} hours.", raStartHours, raEndHours);
            return Result<IReadOnlyList<Star>>.Failure(ex);
        }
    }

    public async Task<Result<IReadOnlyList<Star>>> GetBrightestEverHighAsync(
        double latitudeDeg,
        double minMaxAltitudeDeg = 10.0,
        int topN = 200,
        double magnitudeLimit = 6.5)
    {
        try
        {
            var key = CacheKeys.Build("BrightestHigh", latitudeDeg, minMaxAltitudeDeg, topN, magnitudeLimit);
            if (_cache.TryGetValue(key, out IReadOnlyList<Star>? cached) && cached is not null)
            {
                return Result<IReadOnlyList<Star>>.Success(cached);
            }

            var rows = await _hygContext.Stars
                .Where(s => s.Magnitude != null && s.Magnitude <= magnitudeLimit
                            && s.RightAscensionHours != null && s.DeclinationDegrees != null)
                .Select(s => new { Row = s, Hmax = 90.0 - Math.Abs(latitudeDeg - s.DeclinationDegrees!.Value) })
                .Where(x => x.Hmax >= minMaxAltitudeDeg)
                .OrderBy(x => x.Row.Magnitude)
                .Take(topN)
                .Select(x => x.Row)
                .ToListAsync()
                .ConfigureAwait(false);

            if (rows.Count == 0)
            {
                return Result<IReadOnlyList<Star>>.Success(EmptyStarList);
            }

            var stars = rows.Select(star => MapToStar(star)).ToArray();
            var resultList = Array.AsReadOnly(stars);
            _cache.Set(key, resultList, BrightestCacheOptions);

            return Result<IReadOnlyList<Star>>.Success(resultList);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query brightest stars for latitude {LatitudeDeg}.", latitudeDeg);
            return Result<IReadOnlyList<Star>>.Failure(ex);
        }
    }

    public Task<Result<IReadOnlyDictionary<string, IReadOnlyList<Star>>>> GetConstellationsAsync()
    {
        try
        {
            var catalog = EnsureConstellationCatalog();
            return Task.FromResult(Result<IReadOnlyDictionary<string, IReadOnlyList<Star>>>.Success(catalog.StarLookup));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read constellation catalog.");
            return Task.FromResult(Result<IReadOnlyDictionary<string, IReadOnlyList<Star>>>.Failure(ex));
        }
    }

    public async Task<Result<IReadOnlyList<VisibleConstellation>>> GetVisibleByConstellationAsync(
        double latitudeDeg,
        double longitudeDeg,
        DateTime utc,
        double magnitudeLimit = 6.5,
        double minMaxAltitudeDeg = 10.0,
        int screenWidth = 1,
        int screenHeight = 1,
        StarFieldEngine? engine = null)
    {
        try
        {
            if (engine is null)
            {
                var key = CacheKeys.Build(
                    "VisibleConstellations",
                    RoundUtcToHour(utc),
                    latitudeDeg,
                    longitudeDeg,
                    magnitudeLimit,
                    minMaxAltitudeDeg,
                    screenWidth,
                    screenHeight);

                if (_cache.TryGetValue(key, out IReadOnlyList<VisibleConstellation>? cached) && cached is not null)
                {
                    return Result<IReadOnlyList<VisibleConstellation>>.Success(cached);
                }

                var computed = await ComputeVisibleConstellationsAsync(
                    latitudeDeg,
                    longitudeDeg,
                    utc,
                    magnitudeLimit,
                    minMaxAltitudeDeg,
                    screenWidth,
                    screenHeight,
                    null).ConfigureAwait(false);

                if (computed.IsSuccessful)
                {
                    var value = computed.Value;
                    _cache.Set(key, value, VisibleConstellationCacheOptions);
                }

                return computed;
            }

            return await ComputeVisibleConstellationsAsync(
                latitudeDeg,
                longitudeDeg,
                utc,
                magnitudeLimit,
                minMaxAltitudeDeg,
                screenWidth,
                screenHeight,
                engine).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compute visible constellations for {LatitudeDeg}, {LongitudeDeg} at {Utc}.", latitudeDeg, longitudeDeg, utc);
            return Result<IReadOnlyList<VisibleConstellation>>.Failure(ex);
        }
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
                _logger.LogTrace("Planet visibility request skipped: no bodies enabled.");
                return Task.FromResult(Result<IReadOnlyList<PlanetMark>>.Success(EmptyPlanetMarks));
            }

            var key = CacheKeys.Build(
                "VisiblePlanets",
                RoundUtcToHour(utc),
                latitudeDeg,
                longitudeDeg,
                criteria.IncludePlanets,
                criteria.IncludeMoon,
                criteria.IncludeOuterPlanets,
                criteria.IncludeSun,
                AllowedBodiesToken(criteria.AllowedBodies));

            if (_cache.TryGetValue(key, out IReadOnlyList<PlanetMark>? cached) && cached is not null)
            {
                return Task.FromResult(Result<IReadOnlyList<PlanetMark>>.Success(cached));
            }

            var bodies = criteria.ResolveBodies();
            if (bodies.Count == 0)
            {
                _logger.LogDebug("Planet visibility request resolved zero planet bodies for criteria.");
                return Task.FromResult(Result<IReadOnlyList<PlanetMark>>.Success(EmptyPlanetMarks));
            }

            var marks = PlanetMarks.Compute(latitudeDeg, longitudeDeg, utc, bodies);
            IReadOnlyList<PlanetMark> resultList = marks.Count == 0
                ? EmptyPlanetMarks
                : Array.AsReadOnly(marks.ToArray());

            _cache.Set(key, resultList, VisiblePlanetsCacheOptions);
            return Task.FromResult(Result<IReadOnlyList<PlanetMark>>.Success(resultList));
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

    public IReadOnlyList<ConstellationFigure> GetFigures()
        => EnsureConstellationCatalog().Figures;

    public IReadOnlyDictionary<string, IReadOnlyList<Star>> GetStarLookup()
        => EnsureConstellationCatalog().StarLookup;

    private async Task<Result<IReadOnlyList<Star>>> ComputeVisibleStarsAsync(
        double latitudeDeg,
        double longitudeDeg,
        DateTime utc,
        double magnitudeLimit,
        double minMaxAltitudeDeg,
        int topN,
        bool stratified,
        int raBins,
        int decBands,
        int screenWidth,
        int screenHeight,
        StarFieldEngine? engine)
    {
        var query = await _hygContext.Stars
            .Where(s => s.Magnitude != null && s.Magnitude <= magnitudeLimit
                        && s.RightAscensionHours != null && s.DeclinationDegrees != null)
            .Select(s => new
            {
                s.RightAscensionHours,
                s.DeclinationDegrees,
                s.Magnitude,
                s.SpectralType,
                s.ColorIndexBv,
                s.ProperName,
                s.BayerFlamsteed,
                s.BayerDesignation,
                s.HarvardRevisedId
            })
            .ToListAsync()
            .ConfigureAwait(false);

        if (query.Count == 0)
        {
            return Result<IReadOnlyList<Star>>.Success(EmptyStarList);
        }

        var engineToUse = engine;
        var disposeEngine = false;

        if (engineToUse is null)
        {
            engineToUse = new StarFieldEngine(
                width: screenWidth,
                height: screenHeight,
                latitudeDeg: latitudeDeg,
                longitudeDeg: longitudeDeg,
                utcUtc: utc,
                projectionModel: MockCameraAdapter.DefaultProjectionModel,
                horizonPaddingPct: MockCameraAdapter.DefaultHorizonPadding,
                flipHorizontal: false,
                fovDeg: MockCameraAdapter.DefaultFovDeg,
                applyRefraction: true);
            disposeEngine = true;
        }

        try
        {
            var visible = new List<Star>(query.Count);
            foreach (var row in query)
            {
                var star = new Star(
                    row.RightAscensionHours!.Value,
                    row.DeclinationDegrees!.Value,
                    row.Magnitude!.Value,
                    StarColors.FromCatalog(row.SpectralType, row.ColorIndexBv),
                    ResolveCommonName(row.ProperName),
                    ResolveDesignationName(row.BayerFlamsteed, row.BayerDesignation, row.HarvardRevisedId),
                    row.HarvardRevisedId);

                if (MaxAltitudeDeg(latitudeDeg, star.DeclinationDegrees) < minMaxAltitudeDeg)
                {
                    continue;
                }

                if (!IsVisibleNow(engineToUse, star))
                {
                    continue;
                }

                visible.Add(star);
            }

            if (!visible.Any())
            {
                _logger.LogInformation(
                    "Visible star query returned 0 results after visibility filtering (lat {LatitudeDeg:F3}, lon {LongitudeDeg:F3}, utc {Utc:O}, mag ≤ {MagnitudeLimit:F1}).",
                    latitudeDeg,
                    longitudeDeg,
                    utc,
                    magnitudeLimit);
                return Result<IReadOnlyList<Star>>.Success(EmptyStarList);
            }

            IReadOnlyList<Star> result;
            if (!stratified || raBins <= 0 || decBands <= 0)
            {
                result = Array.AsReadOnly(visible
                    .OrderBy(s => s.Magnitude)
                    .Take(topN)
                    .ToArray());
            }
            else
            {
                var normalizedRaBins = Math.Max(1, raBins);
                var normalizedDecBands = Math.Max(1, decBands);
                const double decMin = -10.0;
                const double decMax = 90.0;

                var buckets = new List<Star>[normalizedRaBins, normalizedDecBands];
                for (var i = 0; i < normalizedRaBins; i++)
                {
                    for (var j = 0; j < normalizedDecBands; j++)
                    {
                        buckets[i, j] = new List<Star>(16);
                    }
                }

                foreach (var star in visible)
                {
                    var raIndex = Math.Clamp((int)Math.Floor((star.RightAscensionHours / 24.0) * normalizedRaBins), 0, normalizedRaBins - 1);
                    var decIndex = Math.Clamp((int)Math.Floor(((star.DeclinationDegrees - decMin) / (decMax - decMin)) * normalizedDecBands), 0, normalizedDecBands - 1);
                    buckets[raIndex, decIndex].Add(star);
                }

                var perBucket = Math.Max(1, topN / (normalizedRaBins * normalizedDecBands));
                var selected = new List<Star>(topN);

                for (var i = 0; i < normalizedRaBins; i++)
                {
                    for (var j = 0; j < normalizedDecBands; j++)
                    {
                        selected.AddRange(buckets[i, j].OrderBy(s => s.Magnitude).Take(perBucket));
                    }
                }

                if (selected.Count < topN)
                {
                    var remainder = buckets
                        .Cast<List<Star>>()
                        .SelectMany(b => b)
                        .Except(selected)
                        .OrderBy(s => s.Magnitude);

                    selected.AddRange(remainder.Take(topN - selected.Count));
                }

                result = Array.AsReadOnly(selected.OrderBy(s => s.RightAscensionHours).ToArray());
            }

            _logger.LogInformation(
                "Computed {Count} visible stars (stratified={Stratified}) for lat {LatitudeDeg:F3}, lon {LongitudeDeg:F3} at {Utc:O} (mag ≤ {MagnitudeLimit:F1}).",
                result.Count,
                stratified,
                latitudeDeg,
                longitudeDeg,
                utc,
                magnitudeLimit);

            return Result<IReadOnlyList<Star>>.Success(result);
        }
        finally
        {
            if (disposeEngine)
            {
                engineToUse?.Dispose();
            }
        }
    }

    private async Task<Result<IReadOnlyList<VisibleConstellation>>> ComputeVisibleConstellationsAsync(
        double latitudeDeg,
        double longitudeDeg,
        DateTime utc,
        double magnitudeLimit,
        double minMaxAltitudeDeg,
        int screenWidth,
        int screenHeight,
        StarFieldEngine? engine)
    {
        var pool = await _hygContext.Stars
            .Where(s => s.Magnitude != null && s.Magnitude <= magnitudeLimit
                        && s.RightAscensionHours != null && s.DeclinationDegrees != null
                        && s.Constellation != null && s.Constellation != "")
            .Select(s => new
            {
                s.Constellation,
                s.RightAscensionHours,
                s.DeclinationDegrees,
                s.Magnitude,
                s.SpectralType,
                s.ColorIndexBv
            })
            .ToListAsync()
            .ConfigureAwait(false);

        if (pool.Count == 0)
        {
            return Result<IReadOnlyList<VisibleConstellation>>.Success(EmptyVisibleConstellations);
        }

        var engineToUse = engine;
        var disposeEngine = false;
        if (engineToUse is null)
        {
            engineToUse = new StarFieldEngine(
                width: screenWidth,
                height: screenHeight,
                latitudeDeg: latitudeDeg,
                longitudeDeg: longitudeDeg,
                utcUtc: utc,
                projectionModel: MockCameraAdapter.DefaultProjectionModel,
                horizonPaddingPct: MockCameraAdapter.DefaultHorizonPadding,
                flipHorizontal: false,
                fovDeg: MockCameraAdapter.DefaultFovDeg,
                applyRefraction: true);
            disposeEngine = true;
        }

        try
        {
            var visible = new List<(string Constellation, Star Star)>(pool.Count);
            foreach (var row in pool)
            {
                if (MaxAltitudeDeg(latitudeDeg, row.DeclinationDegrees!.Value) < minMaxAltitudeDeg)
                {
                    continue;
                }

                var star = new Star(
                    RightAscensionHours: row.RightAscensionHours!.Value,
                    DeclinationDegrees: row.DeclinationDegrees!.Value,
                    Magnitude: row.Magnitude!.Value,
                    Color: StarColors.FromCatalog(row.SpectralType, row.ColorIndexBv));

                if (engineToUse.ProjectStar(star, out _, out _))
                {
                    visible.Add((row.Constellation!, star));
                }
            }

            if (visible.Count == 0)
            {
                return Result<IReadOnlyList<VisibleConstellation>>.Success(EmptyVisibleConstellations);
            }

            var grouped = visible
                .GroupBy(x => x.Constellation)
                .Select(group => new VisibleConstellation
                {
                    ConstellationCode = group.Key,
                    Stars = group.Select(v => v.Star).OrderBy(s => s.Magnitude).ToList()
                })
                .OrderBy(vc => vc.ConstellationCode)
                .ToList();

            return Result<IReadOnlyList<VisibleConstellation>>.Success(Array.AsReadOnly(grouped.ToArray()));
        }
        finally
        {
            if (disposeEngine)
            {
                engineToUse?.Dispose();
            }
        }
    }

    private CatalogCache EnsureConstellationCatalog()
    {
        if (_cache.TryGetValue(CacheKeys.ConstellationCatalog, out CatalogCache? cached) && cached is not null)
        {
            return cached;
        }

        lock (_constellationCacheLock)
        {
            if (_cache.TryGetValue(CacheKeys.ConstellationCatalog, out cached) && cached is not null)
            {
                return cached;
            }

            var catalog = LoadConstellationCatalog();
            _cache.Set(CacheKeys.ConstellationCatalog, catalog, ConstellationCatalogCacheOptions);
            return catalog;
        }
    }

    private CatalogCache LoadConstellationCatalog()
    {
        using var catalogContext = _constellationContextFactory.CreateDbContext();

        var lineEntities = catalogContext.ConstellationLines
            .AsNoTracking()
            .Include(line => line.Stars)
            .ToList();

        if (lineEntities.Count == 0)
        {
            _logger.LogWarning("Constellation catalog database returned no lines. Overlay will be disabled.");
            return CatalogCache.Empty;
        }

        var starIds = lineEntities
            .SelectMany(line => line.Stars)
            .Select(star => star.BscNumber)
            .Distinct()
            .ToArray();

        var starIdSet = new HashSet<int>(starIds);
        var aliasTargetIds = starIds
            .Select(id => HarvardRevisedAliasMap.TryGetValue(id, out var alias) ? alias.SubstituteHarvardRevised : (int?)null)
            .Where(id => id.HasValue && !starIdSet.Contains(id.Value))
            .Select(id => id!.Value)
            .ToArray();

        var lookupHrIds = aliasTargetIds.Length == 0
            ? starIds
            : starIdSet.Concat(aliasTargetIds).ToArray();

        var hygStars = _hygContext.Stars
            .AsNoTracking()
            .Where(star => star.HarvardRevisedId != null && lookupHrIds.Contains(star.HarvardRevisedId.Value))
            .ToList();

        if (hygStars.Count == 0)
        {
            _logger.LogWarning("Unable to resolve any constellation stars from HYG catalog.");
            return CatalogCache.Empty;
        }

        var (starLookup, missing, substituted) = BuildStarLookup(starIds, hygStars);
        if (missing.Count > 0)
        {
            _logger.LogDebug(
                "Constellation catalog: {MissingCount} star(s) could not be resolved in HYG (HR ids: {Ids}).",
                missing.Count,
                string.Join(", ", missing));
        }

        if (substituted.Count > 0)
        {
            _logger.LogDebug(
                "Constellation catalog: {SubstitutedCount} star(s) substituted via HR alias mapping (pairs: {Pairs}).",
                substituted.Count,
                string.Join(", ", substituted.Select(pair => $"{pair.MissingHr}->{pair.SubstituteHr}")));
        }

        var figures = new List<ConstellationFigure>();
        var starLists = new Dictionary<string, IReadOnlyList<Star>>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in lineEntities.GroupBy(line => line.Constellation))
        {
            var abbreviation = group.Key;
            var displayName = ResolveDisplayName(abbreviation);

            var lines = new List<ConstellationFigureLine>();
            var aggregatedStars = new List<Star>();
            var seen = new HashSet<Star>();

            foreach (var line in group.OrderBy(line => line.LineNumber))
            {
                var orderedStars = line.Stars
                    .OrderBy(star => star.SequenceIndex)
                    .Select(star => TryResolveConstellationStar(abbreviation, star, starLookup))
                    .Where(star => star is not null)
                    .Select(star => star!)
                    .ToList();

                if (orderedStars.Count >= 2)
                {
                    lines.Add(new ConstellationFigureLine(line.LineNumber, orderedStars));
                }

                foreach (var star in orderedStars)
                {
                    if (seen.Add(star))
                    {
                        aggregatedStars.Add(star);
                    }
                }
            }

            if (lines.Count == 0)
            {
                _logger.LogWarning("Constellation {Constellation} produced no valid line segments.", abbreviation);
                continue;
            }

            figures.Add(new ConstellationFigure(abbreviation, displayName, lines));
            starLists[displayName] = Array.AsReadOnly(aggregatedStars.ToArray());
        }

        return figures.Count == 0
            ? CatalogCache.Empty
            : new CatalogCache(Array.AsReadOnly(figures.ToArray()), new ReadOnlyDictionary<string, IReadOnlyList<Star>>(starLists));
    }

    private static (IReadOnlyDictionary<int, Star> Lookup, List<int> Missing, List<(int MissingHr, int SubstituteHr)> Substituted) BuildStarLookup(
        IReadOnlyCollection<int> starIds,
        IReadOnlyCollection<HygStar> hygStars)
    {
        var result = new Dictionary<int, Star>();
        var missing = new List<int>();
        var substituted = new List<(int MissingHr, int SubstituteHr)>();

        var hygByHr = hygStars
            .Where(star => star.HarvardRevisedId.HasValue)
            .GroupBy(star => star.HarvardRevisedId!.Value)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var id in starIds)
        {
            if (!hygByHr.TryGetValue(id, out var starEntity))
            {
                if (HarvardRevisedAliasMap.TryGetValue(id, out var alias) && hygByHr.TryGetValue(alias.SubstituteHarvardRevised, out var aliasEntity))
                {
                    result[id] = MapToStar(aliasEntity, id);
                    substituted.Add((id, alias.SubstituteHarvardRevised));
                    continue;
                }

                missing.Add(id);
                continue;
            }

            result[id] = MapToStar(starEntity);
        }

        return (result, missing, substituted);
    }

    private static string ResolveDisplayName(string abbreviation)
    {
        if (ConstellationNameLookup.TryGetValue(abbreviation, out var displayName))
        {
            return displayName;
        }

        return abbreviation;
    }

    private static Star? TryResolveConstellationStar(
        string abbreviation,
        ConstellationLineStarEntity lineStar,
        IReadOnlyDictionary<int, Star> lookup)
    {
        if (!lookup.TryGetValue(lineStar.BscNumber, out var constellationStar))
        {
            return null;
        }

        return constellationStar;
    }

    private static string? ResolveDesignationName(HygStar star)
        => ResolveDesignationName(star.BayerFlamsteed, star.BayerDesignation, star.HarvardRevisedId);

    private static string? ResolveDesignationName(string? bayerFlamsteed, string? bayerDesignation, int? harvardRevisedId)
    {
        if (!string.IsNullOrWhiteSpace(bayerFlamsteed))
        {
            return bayerFlamsteed;
        }

        if (!string.IsNullOrWhiteSpace(bayerDesignation))
        {
            return bayerDesignation;
        }

        return harvardRevisedId is int hr ? $"HR {hr}" : null;
    }

    private static string? ResolveCommonName(HygStar star)
        => ResolveCommonName(star.ProperName);

    private static string? ResolveCommonName(string? properName)
    {
        var proper = properName?.Trim();
        return string.IsNullOrEmpty(proper) ? null : proper;
    }

    private static double MaxAltitudeDeg(double latDeg, double decDeg)
        => 90.0 - Math.Abs(latDeg - decDeg);

    private static Star MapToStar(HygStar s, int? overrideHarvardRevisedId = null)
        => new(
            RightAscensionHours: s.RightAscensionHours!.Value,
            DeclinationDegrees: s.DeclinationDegrees!.Value,
            Magnitude: s.Magnitude!.Value,
            Color: StarColors.FromCatalog(s.SpectralType, s.ColorIndexBv),
            CommonName: ResolveCommonName(s),
            Designation: ResolveDesignationName(s),
            HarvardRevisedNumber: overrideHarvardRevisedId ?? s.HarvardRevisedId);

    private static bool IsVisibleNow(StarFieldEngine engine, in Star star)
        => engine.ProjectStar(star, out _, out _);

    private static DateTime RoundUtcToHour(DateTime utc)
    {
        var utcValue = utc.ToUniversalTime();
        return new DateTime(utcValue.Year, utcValue.Month, utcValue.Day, utcValue.Hour, 0, 0, DateTimeKind.Utc);
    }

    private static string AllowedBodiesToken(IReadOnlyCollection<PlanetBody>? bodies)
    {
        if (bodies is null || bodies.Count == 0)
        {
            return "*";
        }

        return string.Join('-', bodies.OrderBy(b => b).Select(b => b.ToString()));
    }

    private static MemoryCacheEntryOptions CreateCacheEntryOptions(TimeSpan absolute, TimeSpan sliding)
    {
        return new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = absolute,
            SlidingExpiration = sliding,
            Size = 1
        };
    }

    private sealed record HarvardAlias(int SubstituteHarvardRevised, int HipparcosId, string Note);

    private sealed record CatalogCache(
        IReadOnlyList<ConstellationFigure> Figures,
        IReadOnlyDictionary<string, IReadOnlyList<Star>> StarLookup)
    {
        public static CatalogCache Empty { get; } = new(
            Array.AsReadOnly(Array.Empty<ConstellationFigure>()),
            new ReadOnlyDictionary<string, IReadOnlyList<Star>>(new Dictionary<string, IReadOnlyList<Star>>(StringComparer.OrdinalIgnoreCase)));
    }

    private static class CacheKeys
    {
        public const string ConstellationCatalog = "SkyMonitorRepository::ConstellationCatalog";

        public static string Build(string category, params object?[] parts)
        {
            var builder = new StringBuilder("SkyMonitorRepository::");
            builder.Append(category);

            foreach (var part in parts)
            {
                builder.Append('|');
                switch (part)
                {
                    case null:
                        builder.Append("null");
                        break;
                    case DateTime dt:
                        builder.Append(dt.ToUniversalTime().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture));
                        break;
                    case double d:
                        builder.Append(d.ToString("F6", CultureInfo.InvariantCulture));
                        break;
                    case float f:
                        builder.Append(f.ToString("F4", CultureInfo.InvariantCulture));
                        break;
                    case bool b:
                        builder.Append(b ? '1' : '0');
                        break;
                    default:
                        builder.Append(part);
                        break;
                }
            }

            return builder.ToString();
        }
    }
}
