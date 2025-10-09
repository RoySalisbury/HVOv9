using Microsoft.EntityFrameworkCore;

namespace HVO.SkyMonitorV5.RPi.Data;

public sealed class ConstellationCatalogContext : DbContext
{
    public ConstellationCatalogContext(DbContextOptions<ConstellationCatalogContext> options)
        : base(options)
    {
    }

    public DbSet<ConstellationLineEntity> ConstellationLines => Set<ConstellationLineEntity>();
    public DbSet<ConstellationLineStarEntity> ConstellationLineStars => Set<ConstellationLineStarEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConstellationLineEntity>(entity =>
        {
            entity.ToTable("constellation_line");

            entity.HasKey(e => e.LineId);

            entity.Property(e => e.LineId).HasColumnName("line_id");
            entity.Property(e => e.Constellation).HasColumnName("constellation");
            entity.Property(e => e.LineNumber).HasColumnName("line_number");
            entity.Property(e => e.StarCount).HasColumnName("star_count");

            entity.HasMany(e => e.Stars)
                .WithOne(s => s.Line)
                .HasForeignKey(s => s.LineId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ConstellationLineStarEntity>(entity =>
        {
            entity.ToTable("constellation_line_star");

            entity.HasKey(e => new { e.LineId, e.SequenceIndex });

            entity.Property(e => e.LineId).HasColumnName("line_id");
            entity.Property(e => e.SequenceIndex).HasColumnName("sequence_index");
            entity.Property(e => e.BscNumber).HasColumnName("bsc_number");
        });
    }
}
