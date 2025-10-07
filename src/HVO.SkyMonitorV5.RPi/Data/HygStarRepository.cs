using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.RPi.Cameras.MockCamera;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVO.SkyMonitorV5.RPi.Data;


public sealed class HygStarRepository : IStarRepository
{
    private readonly HygContext _db;
    private readonly IConstellationCatalog _constellationCatalog;
    private readonly ILogger<HygStarRepository> _logger;
    private readonly ICelestialProjector _celestialProjector;

    public HygStarRepository(
        HygContext db,
        IConstellationCatalog constellationCatalog,
        ICelestialProjector celestialProjector,
        ILogger<HygStarRepository>? logger = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _constellationCatalog = constellationCatalog ?? throw new ArgumentNullException(nameof(constellationCatalog));
        _celestialProjector = celestialProjector ?? throw new ArgumentNullException(nameof(celestialProjector));
        _logger = logger ?? NullLogger<HygStarRepository>.Instance;
    }

    // --- Helpers ---
    private static double MaxAltitudeDeg(double latDeg, double decDeg) => 90.0 - Math.Abs(latDeg - decDeg);

    private static Star MapToStar(HygStar s)
    => new Star(
        RightAscensionHours: s.RightAscensionHours!.Value,
        DeclinationDegrees: s.DeclinationDegrees!.Value,
        Magnitude: s.Magnitude!.Value,
        Color: StarColors.FromCatalog(s.SpectralType, s.ColorIndexBv)
    );

    // Visible "now" using your StarFieldEngine.ProjectStar(..)
    private static bool IsVisibleNow(StarFieldEngine engine, in Star star)
        => engine.ProjectStar(star, out _, out _);

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
        try
        {
            var pool = await _db.Stars
                .Where(s => s.Magnitude != null && s.Magnitude <= magnitudeLimit
                            && s.RightAscensionHours != null && s.DeclinationDegrees != null)
                .Select(s => new
                {
                    s.RightAscensionHours,
                    s.DeclinationDegrees,
                    s.Magnitude,
                    s.SpectralType,
                    s.ColorIndexBv
                })
                .ToListAsync()
                .ConfigureAwait(false);

            var projector = new StarFieldEngine(
                width: Math.Max(1, screenWidth),
                height: Math.Max(1, screenHeight),
                latitudeDeg: latitudeDeg,
                longitudeDeg: longitudeDeg,
                utcUtc: utc,
                projector: _celestialProjector);

            var visible = new List<Star>(pool.Count);
            foreach (var row in pool)
            {
                var star = new Star(
                    row.RightAscensionHours!.Value,
                    row.DeclinationDegrees!.Value,
                    row.Magnitude!.Value,
                    StarColors.FromCatalog(row.SpectralType, row.ColorIndexBv));

                if (MaxAltitudeDeg(latitudeDeg, star.DeclinationDegrees) < minMaxAltitudeDeg)
                {
                    continue;
                }

                if (!IsVisibleNow(projector, star))
                {
                    continue;
                }

                visible.Add(star);
            }

            IReadOnlyList<Star> result;
            if (!stratified || raBins <= 0 || decBands <= 0)
            {
                result = visible.OrderBy(s => s.Magnitude).Take(topN).ToList();
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

                result = selected.OrderBy(s => s.RightAscensionHours).ToList();
            }

            _logger.LogDebug("Computed {Count} visible stars (stratified={Stratified}) for {LatitudeDeg}, {LongitudeDeg} at {Utc}.",
                result.Count, stratified, latitudeDeg, longitudeDeg, utc);

            return Result<IReadOnlyList<Star>>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compute visible stars for {LatitudeDeg}, {LongitudeDeg} at {Utc}.",
                latitudeDeg, longitudeDeg, utc);
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
                return Result<IReadOnlyList<Star>>.Success(Array.Empty<Star>());
            }

            var rows = await _db.Stars
                .Where(s => s.Constellation == code && s.Magnitude != null && s.Magnitude <= magnitudeLimit
                            && s.RightAscensionHours != null && s.DeclinationDegrees != null)
                .OrderBy(s => s.Magnitude)
                .ToListAsync()
                .ConfigureAwait(false);

            var stars = rows.Select(MapToStar).ToList();
            return Result<IReadOnlyList<Star>>.Success(stars);
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

            var matches = await _db.Stars
                .Where(s =>
                    (s.ProperName != null && EF.Functions.Like(s.ProperName, $"%{term}%")) ||
                    (s.BayerFlamsteed != null && EF.Functions.Like(s.BayerFlamsteed, $"%{term}%")))
                .OrderBy(s => s.Magnitude)
                .Take(limit)
                .ToListAsync()
                .ConfigureAwait(false);

            return Result<IReadOnlyList<HygStar>>.Success(matches);
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
            var query = _db.Stars.Where(s => s.Magnitude != null && s.Magnitude <= magnitudeLimit
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
            var stars = rows.Select(MapToStar).ToList();
            return Result<IReadOnlyList<Star>>.Success(stars);
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
            var rows = await _db.Stars
                .Where(s => s.Magnitude != null && s.Magnitude <= magnitudeLimit
                            && s.RightAscensionHours != null && s.DeclinationDegrees != null)
                .Select(s => new { Row = s, Hmax = 90.0 - Math.Abs(latitudeDeg - s.DeclinationDegrees!.Value) })
                .Where(x => x.Hmax >= minMaxAltitudeDeg)
                .OrderBy(x => x.Row.Magnitude)
                .Take(topN)
                .Select(x => x.Row)
                .ToListAsync()
                .ConfigureAwait(false);

            var stars = rows.Select(MapToStar).ToList();
            return Result<IReadOnlyList<Star>>.Success(stars);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query brightest stars for latitude {LatitudeDeg}.", latitudeDeg);
            return Result<IReadOnlyList<Star>>.Failure(ex);
        }
    }

    public Task<Result<IReadOnlyDictionary<string, IReadOnlyList<ConstellationStar>>>> GetConstellationsAsync()
    {
        try
        {
            var catalog = _constellationCatalog.GetAll();
            return Task.FromResult(Result<IReadOnlyDictionary<string, IReadOnlyList<ConstellationStar>>>.Success(catalog));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read constellation catalog.");
            return Task.FromResult(Result<IReadOnlyDictionary<string, IReadOnlyList<ConstellationStar>>>.Failure(ex));
        }
    }

    public async Task<Result<IReadOnlyList<VisibleConstellation>>> GetVisibleByConstellationAsync(
        double latitudeDeg,
        double longitudeDeg,
        DateTime utc,
        double magnitudeLimit = 6.5,
        double minMaxAltitudeDeg = 10.0,
        int screenWidth = 1,
        int screenHeight = 1)
    {
        try
        {
            var pool = await _db.Stars
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

            var engine = new StarFieldEngine(
                width: Math.Max(1, screenWidth),
                height: Math.Max(1, screenHeight),
                latitudeDeg: latitudeDeg,
                longitudeDeg: longitudeDeg,
                utcUtc: utc,
                projector: _celestialProjector);

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

                if (engine.ProjectStar(star, out _, out _))
                {
                    visible.Add((row.Constellation!, star));
                }
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

            return Result<IReadOnlyList<VisibleConstellation>>.Success(grouped);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compute visible constellations for {LatitudeDeg}, {LongitudeDeg} at {Utc}.", latitudeDeg, longitudeDeg, utc);
            return Result<IReadOnlyList<VisibleConstellation>>.Failure(ex);
        }
    }


}
