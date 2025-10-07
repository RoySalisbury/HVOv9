using System;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HVO.SkyMonitorV5.RPi.Data;

public class HygContext : DbContext
{
    public DbSet<HygStar> Stars => Set<HygStar>();
    public HygContext(DbContextOptions<HygContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<HygStar>().HasIndex(x => new { x.RightAscensionHours, x.DeclinationDegrees })
            .HasDatabaseName("idx_hyg_stars_radec");
        mb.Entity<HygStar>().HasIndex(x => x.Magnitude)
            .HasDatabaseName("idx_hyg_stars_mag");
        mb.Entity<HygStar>().HasIndex(x => x.Constellation)
            .HasDatabaseName("idx_hyg_stars_con");
    }
}

[Table("hyg_stars")]
public class HygStar
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("hip")]
    public int? HipparcosId { get; set; }

    [Column("hd")]
    public int? HenryDraperId { get; set; }

    [Column("hr")]
    public int? HarvardRevisedId { get; set; }

    [Column("gl")]
    public string? GlieseId { get; set; }

    [Column("bf")]
    public string? BayerFlamsteed { get; set; }

    [Column("proper")]
    public string? ProperName { get; set; }

    [Column("ra_hours")]
    public double? RightAscensionHours { get; set; }

    [Column("dec_deg")]
    public double? DeclinationDegrees { get; set; }

    [Column("dist_pc")]
    public double? DistanceParsecs { get; set; }

    [Column("pmra")]
    public double? ProperMotionRa { get; set; }

    [Column("pmdec")]
    public double? ProperMotionDec { get; set; }

    [Column("rv")]
    public double? RadialVelocity { get; set; }

    [Column("mag")]
    public double? Magnitude { get; set; }

    [Column("absmag")]
    public double? AbsoluteMagnitude { get; set; }

    [Column("spect")]
    public string? SpectralType { get; set; }

    [Column("ci")]
    public double? ColorIndexBv { get; set; }

    [Column("rarad")]
    public double? RightAscensionRadians { get; set; }

    [Column("decrad")]
    public double? DeclinationRadians { get; set; }

    [Column("bayer")]
    public string? BayerDesignation { get; set; }

    [Column("flam")]
    public string? FlamsteedNumber { get; set; }

    [Column("con")]
    public string? Constellation { get; set; }

    [Column("lum")]
    public double? Luminosity { get; set; }

    [Column("var")]
    public string? VariableStarDesignation { get; set; }

    [Column("var_min")]
    public double? VariableMinMagnitude { get; set; }

    [Column("var_max")]
    public double? VariableMaxMagnitude { get; set; }
}

/* 

1) Brightest stars that ever get reasonably high at your latitude
double latitudeDeg = 36.1699;   // Las Vegas
double minMaxAltitudeDeg = 10.0; // must reach ≥10° at some time of year

using var db = new HygContext(
    new DbContextOptionsBuilder<HygContext>().UseSqlite("Data Source=hyg_v42.sqlite").Options);

// h_max = 90° - |φ - δ|
var candidates = await db.Stars
    .Where(s => s.Magnitude != null && s.DeclinationDegrees != null)
    .Select(s => new
    {
        Star = s,
        MaxAlt = 90.0 - Math.Abs(latitudeDeg - s.DeclinationDegrees!.Value)
    })
    .Where(x => x.MaxAlt >= minMaxAltitudeDeg)
    .OrderBy(x => x.Star.Magnitude)   // brightest first
    .Take(200)
    .Select(x => x.Star)
    .ToListAsync();
*/

/*

2) Constellation slice (example: Orion)
var orion = await db.Stars
    .Where(s => s.Constellation == "Ori" && s.Magnitude != null && s.Magnitude <= 6.0)
    .OrderBy(s => s.Magnitude)
    .ToListAsync();

*/


/*

3) RA window (example: 2h ≤ RA < 4h), magnitude-limited
var raSlice = await db.Stars
    .Where(s => s.RightAscensionHours != null && s.DeclinationDegrees != null && s.Magnitude != null)
    .Where(s => s.RightAscensionHours! >= 2.0 && s.RightAscensionHours! < 4.0 && s.Magnitude! <= 5.5)
    .OrderBy(s => s.DeclinationDegrees)
    .ToListAsync();

*/

/*

4) “Visible right now” set for your renderer

Use your StarFieldEngine.ProjectStar(...) to keep only stars above the horizon inside the fisheye.
This example also converts DB rows → your Star record with spectral tinting.

// Your Star record and SpectralColor helper (assumed to exist):
// public record Star(double RaHours, double DecDeg, double Mag, SKColor? Color = null);
// SKColor SpectralColor(string? spectral) { ... }

double lat = 36.1699;
double lon = -115.1398;
var whenUtc = DateTime.UtcNow;

// Pull a practical set first (mag ≤ 6.5 and ever reaches ≥10° altitude)
var prelim = await db.Stars
    .Where(s => s.Magnitude != null && s.Magnitude <= 6.5 && s.RightAscensionHours != null && s.DeclinationDegrees != null)
    .Select(s => new
    {
        s.RightAscensionHours,
        s.DeclinationDegrees,
        s.Magnitude,
        s.SpectralType
    })
    .ToListAsync();

// Filter in-memory to “currently visible”
var engine = new StarFieldEngine(1920, 1080, lat, lon, whenUtc);
var visible = new List<Star>(prelim.Count);
foreach (var r in prelim)
{
    var star = new Star(
        RaHours: r.RightAscensionHours!.Value,
        DecDeg:  r.DeclinationDegrees!.Value,
        Mag:     r.Magnitude!.Value,
        Color:   SpectralColor(r.SpectralType)
    );
    if (engine.ProjectStar(star, out _, out _))
        visible.Add(star);
}

// Take the best N (by brightness), or do your stratified spread afterwards
var bestNow = visible.OrderBy(s => s.Mag).Take(300).ToList();

// Render
var bmp = engine.Render(bestNow, randomFillerCount: 0, randomSeed: null, dimFaintStars: true, out _);

*/

/*

5) Even sky coverage (stratified) after querying

If you want an even spread across the dome (not just brightest), stratify by RA/Dec buckets:

int raBins = 24, decBands = 8;
double decMin = -10, decMax = +90; // northern-friendly banding

var pool = await db.Stars
    .Where(s => s.Magnitude != null && s.Magnitude <= 6.0 && s.RightAscensionHours != null && s.DeclinationDegrees != null)
    .Select(s => new { s.RightAscensionHours, s.DeclinationDegrees, s.Magnitude, s.SpectralType })
    .ToListAsync();

var buckets = new List<Star>[raBins, decBands];
for (int i = 0; i < raBins; i++)
  for (int j = 0; j < decBands; j++)
    buckets[i,j] = new List<Star>(16);

foreach (var r in pool)
{
    var star = new Star(r.RightAscensionHours!.Value, r.DeclinationDegrees!.Value, r.Magnitude!.Value, SpectralColor(r.SpectralType));
    if (!new StarFieldEngine(1,1,lat,lon,whenUtc).ProjectStar(star, out _, out _)) continue; // visible now

    int i = Math.Clamp((int)Math.Floor((star.RaHours / 24.0) * raBins), 0, raBins - 1);
    int j = Math.Clamp((int)Math.Floor(((star.DecDeg - decMin) / (decMax - decMin)) * decBands), 0, decBands - 1);
    buckets[i,j].Add(star);
}

// pick evenly from each bucket
int target = 300;
int perBucket = Math.Max(1, target / (raBins * decBands));
var selected = new List<Star>(target);

for (int i = 0; i < raBins; i++)
for (int j = 0; j < decBands; j++)
    selected.AddRange(buckets[i,j].OrderBy(s => s.Mag).Take(perBucket));

if (selected.Count < target)
{
    var remainder = buckets.Cast<List<Star>>().SelectMany(b => b).Except(selected).OrderBy(s => s.Mag);
    selected.AddRange(remainder.Take(target - selected.Count));
}

selected = selected.OrderBy(s => s.RaHours).ToList();


*/

/*

6) Name search (proper/common/Bayer–Flamsteed)
string q = "Betelgeuse";
var matches = await db.Stars
    .Where(s =>
        (s.ProperName != null && EF.Functions.Like(s.ProperName, $"%{q}%")) ||
        (s.BayerFlamsteed != null && EF.Functions.Like(s.BayerFlamsteed, $"%{q}%")))
    .OrderBy(s => s.Magnitude)
    .Take(20)
    .ToListAsync();

*/