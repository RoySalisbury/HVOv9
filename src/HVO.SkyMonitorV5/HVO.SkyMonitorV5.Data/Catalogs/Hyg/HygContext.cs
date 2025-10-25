using Microsoft.EntityFrameworkCore;

namespace HVO.SkyMonitorV5.Data.Catalogs.Hyg;

public sealed class HygContext : DbContext
{
    public HygContext(DbContextOptions<HygContext> options)
        : base(options)
    {
    }

    public DbSet<HygStar> Stars => Set<HygStar>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<HygStar>(entity =>
        {
            entity.ToTable("hyg_stars");

            entity.HasKey(e => e.Id);

            entity.HasIndex(e => new { e.RightAscensionHours, e.DeclinationDegrees })
                .HasDatabaseName("idx_hyg_stars_radec");
            entity.HasIndex(e => e.Magnitude)
                .HasDatabaseName("idx_hyg_stars_mag");
            entity.HasIndex(e => e.Constellation)
                .HasDatabaseName("idx_hyg_stars_con");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.HipparcosId).HasColumnName("hip");
            entity.Property(e => e.HenryDraperId).HasColumnName("hd");
            entity.Property(e => e.HarvardRevisedId).HasColumnName("hr");
            entity.Property(e => e.GlieseId).HasColumnName("gl");
            entity.Property(e => e.BayerFlamsteed).HasColumnName("bf");
            entity.Property(e => e.ProperName).HasColumnName("proper");
            entity.Property(e => e.RightAscensionHours).HasColumnName("ra_hours");
            entity.Property(e => e.DeclinationDegrees).HasColumnName("dec_deg");
            entity.Property(e => e.DistanceParsecs).HasColumnName("dist_pc");
            entity.Property(e => e.ProperMotionRa).HasColumnName("pmra");
            entity.Property(e => e.ProperMotionDec).HasColumnName("pmdec");
            entity.Property(e => e.RadialVelocity).HasColumnName("rv");
            entity.Property(e => e.Magnitude).HasColumnName("mag");
            entity.Property(e => e.AbsoluteMagnitude).HasColumnName("absmag");
            entity.Property(e => e.SpectralType).HasColumnName("spect");
            entity.Property(e => e.ColorIndexBv).HasColumnName("ci");
            entity.Property(e => e.RightAscensionRadians).HasColumnName("rarad");
            entity.Property(e => e.DeclinationRadians).HasColumnName("decrad");
            entity.Property(e => e.BayerDesignation).HasColumnName("bayer");
            entity.Property(e => e.FlamsteedNumber).HasColumnName("flam");
            entity.Property(e => e.Constellation).HasColumnName("con");
            entity.Property(e => e.Luminosity).HasColumnName("lum");
            entity.Property(e => e.VariableStarDesignation).HasColumnName("var");
            entity.Property(e => e.VariableMinMagnitude).HasColumnName("var_min");
            entity.Property(e => e.VariableMaxMagnitude).HasColumnName("var_max");
        });
    }
}