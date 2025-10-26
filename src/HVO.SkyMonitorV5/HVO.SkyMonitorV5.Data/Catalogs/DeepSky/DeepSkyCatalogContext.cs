using Microsoft.EntityFrameworkCore;

namespace HVO.SkyMonitorV5.Data.Catalogs.DeepSky;

public sealed class DeepSkyCatalogContext : DbContext
{
    public DeepSkyCatalogContext(DbContextOptions<DeepSkyCatalogContext> options)
        : base(options)
    {
    }

    public DbSet<DeepSkyObjectEntity> DeepSkyObjects => Set<DeepSkyObjectEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DeepSkyObjectEntity>(entity =>
        {
            entity.ToTable("deep_sky_object");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.PrimaryId).HasColumnName("primary_id");
            entity.Property(e => e.CommonName).HasColumnName("common_name");
            entity.Property(e => e.Constellation).HasColumnName("constellation");
            entity.Property(e => e.RightAscensionHours).HasColumnName("right_ascension_hours");
            entity.Property(e => e.DeclinationDegrees).HasColumnName("declination_degrees");
            entity.Property(e => e.ApparentMagnitude).HasColumnName("apparent_magnitude");
            entity.Property(e => e.ObjectType).HasColumnName("object_type");

            entity.HasIndex(e => e.PrimaryId).IsUnique();
            entity.HasIndex(e => e.Constellation);
            entity.HasIndex(e => e.RightAscensionHours);
            entity.HasIndex(e => e.DeclinationDegrees);
            entity.HasIndex(e => e.ApparentMagnitude);
        });
    }
}
