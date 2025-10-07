using System;
using System.Collections.Generic;
using HVO.SkyMonitorV5.RPi.Cameras.MockCamera;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Data;


public sealed class HygStarRepository : IStarRepository
{
    private readonly HygContext _db;

    public HygStarRepository(HygContext db) => _db = db;

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

    public async Task<List<Star>> GetVisibleStarsAsync(
        double latitudeDeg,
        double longitudeDeg,
        DateTime utc,
        double magnitudeLimit = 6.5,
        double minMaxAltitudeDeg = 10.0,
        int topN = 500,
        bool stratified = false,
        int raBins = 24,
        int decBands = 8,
        int screenWidth = 1,
        int screenHeight = 1)
    {
        // Pre-pool: mag limit + "ever reasonably high" from the site
        var pool = await _db.Stars
            .Where(s => s.Magnitude != null && s.Magnitude <= magnitudeLimit
                        && s.RightAscensionHours != null && s.DeclinationDegrees != null)
            .Select(s => new
            {
                s.RightAscensionHours,
                s.DeclinationDegrees,
                s.Magnitude,
                s.SpectralType
            })
            .OrderBy(s => s.Magnitude)
            .ToListAsync();

        // Instantiate a tiny projector (1x1 is enough; projection truth is the same)
        var projector = new StarFieldEngine(
            width: Math.Max(1, screenWidth),
            height: Math.Max(1, screenHeight),
            latitudeDeg: latitudeDeg,
            longitudeDeg: longitudeDeg,
            utcUtc: utc
        );

        // Filter to stars that both: ever get high enough, and are above horizon now
        var visible = new List<Star>(pool.Count);
        foreach (var r in pool)
        {
            var star = new Star(r.RightAscensionHours!.Value, r.DeclinationDegrees!.Value, r.Magnitude!.Value, StarColors.SpectralColor(r.SpectralType));
            if (MaxAltitudeDeg(latitudeDeg, star.DeclinationDegrees) < minMaxAltitudeDeg) continue;
            if (!IsVisibleNow(projector, star)) continue;
            visible.Add(star);
        }

        if (!stratified)
            return visible.OrderBy(s => s.Magnitude).Take(topN).ToList();

        // Stratified RA/Dec selection for nice even sky coverage
        double decMin = -10.0, decMax = +90.0; // northern-friendly banding
        var buckets = new List<Star>[raBins, decBands];
        for (int i = 0; i < raBins; i++)
            for (int j = 0; j < decBands; j++)
                buckets[i, j] = new List<Star>(16);

        foreach (var s in visible)
        {
            int i = Math.Clamp((int)Math.Floor((s.RightAscensionHours / 24.0) * raBins), 0, raBins - 1);
            int j = Math.Clamp((int)Math.Floor(((s.DeclinationDegrees - decMin) / (decMax - decMin)) * decBands), 0, decBands - 1);
            buckets[i, j].Add(s);
        }

        int perBucket = Math.Max(1, topN / (raBins * decBands));
        var selected = new List<Star>(topN);

        for (int i = 0; i < raBins; i++)
            for (int j = 0; j < decBands; j++)
                selected.AddRange(buckets[i, j].OrderBy(s => s.Magnitude).Take(perBucket));

        if (selected.Count < topN)
        {
            var remainder = buckets.Cast<List<Star>>().SelectMany(b => b).Except(selected).OrderBy(s => s.Magnitude);
            selected.AddRange(remainder.Take(topN - selected.Count));
        }

        return selected.OrderBy(s => s.RightAscensionHours).ToList();
    }

    public async Task<List<Star>> GetConstellationStarsAsync(string constellation3, double magnitudeLimit = 6.0)
    {
        // Constellation codes are 3-letter IAU (e.g., "Ori", "CMa", "UMa")
        constellation3 = constellation3?.Trim() ?? "";
        var rows = await _db.Stars
            .Where(s => s.Constellation == constellation3 && s.Magnitude != null && s.Magnitude <= magnitudeLimit
                        && s.RightAscensionHours != null && s.DeclinationDegrees != null)
            .OrderBy(s => s.Magnitude)
            .ToListAsync();

        return rows.Select(MapToStar).ToList();
    }

    public async Task<List<HygStar>> SearchByNameAsync(string query, int limit = 20)
    {
        query = query?.Trim() ?? "";
        if (string.IsNullOrEmpty(query)) return new List<HygStar>();

        return await _db.Stars
            .Where(s =>
                (s.ProperName != null && EF.Functions.Like(s.ProperName, $"%{query}%")) ||
                (s.BayerFlamsteed != null && EF.Functions.Like(s.BayerFlamsteed, $"%{query}%")))
            .OrderBy(s => s.Magnitude)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<Star>> GetRaWindowAsync(double raStartHours, double raEndHours, double magnitudeLimit = 6.0)
    {
        // Handles wrap-around if raEnd < raStart
        var q = _db.Stars.Where(s => s.Magnitude != null && s.Magnitude <= magnitudeLimit
                                     && s.RightAscensionHours != null && s.DeclinationDegrees != null);

        if (raEndHours >= raStartHours)
            q = q.Where(s => s.RightAscensionHours! >= raStartHours && s.RightAscensionHours! < raEndHours);
        else
            q = q.Where(s => s.RightAscensionHours! >= raStartHours || s.RightAscensionHours! < raEndHours);

        var rows = await q.OrderBy(s => s.DeclinationDegrees).ToListAsync();
        return rows.Select(MapToStar).ToList();
    }

    public async Task<List<Star>> GetBrightestEverHighAsync(
        double latitudeDeg,
        double minMaxAltitudeDeg = 10.0,
        int topN = 200,
        double magnitudeLimit = 6.5)
    {
        var rows = await _db.Stars
            .Where(s => s.Magnitude != null && s.Magnitude <= magnitudeLimit
                        && s.RightAscensionHours != null && s.DeclinationDegrees != null)
            .Select(s => new { Row = s, Hmax = 90.0 - Math.Abs(latitudeDeg - s.DeclinationDegrees!.Value) })
            .Where(x => x.Hmax >= minMaxAltitudeDeg)
            .OrderBy(x => x.Row.Magnitude)
            .Take(topN)
            .Select(x => x.Row)
            .ToListAsync();

        return rows.Select(MapToStar).ToList();
    }

    public Task<IReadOnlyDictionary<string, IReadOnlyList<ConstellationStar>>> GetConstellationsAsync()
    {
        return Task.FromResult(ConstellationCatalog.All);
    }

    public async Task<List<VisibleConstellation>> GetVisibleByConstellationAsync(
        double latitudeDeg,
        double longitudeDeg,
        DateTime utc,
        double magnitudeLimit = 6.5,
        double minMaxAltitudeDeg = 10.0,   // must be able to reach this altitude sometime
        int screenWidth = 1,
        int screenHeight = 1)
    {
        // Pull a practical candidate set (only cols we need)
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
            .ToListAsync();

        // Tiny projector (1x1 is fine; projection test is the same)
        var engine = new StarFieldEngine(
            width: Math.Max(1, screenWidth),
            height: Math.Max(1, screenHeight),
            latitudeDeg: latitudeDeg,
            longitudeDeg: longitudeDeg,
            utcUtc: utc);

        static double MaxAltitudeDeg(double latDeg, double decDeg) => 90.0 - Math.Abs(latDeg - decDeg);

        // Filter to: (a) ever reasonably high, (b) visible now
        var visible = new List<(string con, Star star)>(pool.Count);
        foreach (var r in pool)
        {
            if (MaxAltitudeDeg(latitudeDeg, r.DeclinationDegrees!.Value) < minMaxAltitudeDeg)
                continue;

            var star = new Star(
                RightAscensionHours: r.RightAscensionHours!.Value,
                DeclinationDegrees: r.DeclinationDegrees!.Value,
                Magnitude: r.Magnitude!.Value,
                Color: StarColors.FromCatalog(r.SpectralType, r.ColorIndexBv)
            );

            if (engine.ProjectStar(star, out _, out _))
                visible.Add((r.Constellation!, star));
        }

        // Group by constellation code and sort each star list by brightness
        var grouped = visible
            .GroupBy(x => x.con)
            .Select(g => new VisibleConstellation
            {
                ConstellationCode = g.Key,
                Stars = g.Select(v => v.star).OrderBy(s => s.Magnitude).ToList()
            })
            .OrderBy(vc => vc.ConstellationCode)
            .ToList();

        return grouped;
    }


}
