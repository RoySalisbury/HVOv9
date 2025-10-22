using System;
using System.Linq;
using System.Text.Json;
using HVO.SkyMonitorV5.Data.Archive.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HVO.SkyMonitorV5.Data.Archive;

/// <summary>
/// SQLite-backed context that stores metadata and storage references for processed frames archived for the Image History experience.
/// </summary>
public sealed class ImageFrameArchiveContext : DbContext
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public ImageFrameArchiveContext(DbContextOptions<ImageFrameArchiveContext> options)
        : base(options)
    {
    }

    public DbSet<FrameArchiveEntity> FrameArchives => Set<FrameArchiveEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureFrameArchiveEntity(modelBuilder);
    }

    private static void ConfigureFrameArchiveEntity(ModelBuilder modelBuilder)
    {
        var filtersConverter = new ValueConverter<string[], string>(
            value => JsonSerializer.Serialize(value ?? Array.Empty<string>(), SerializerOptions),
            value => string.IsNullOrWhiteSpace(value)
                ? Array.Empty<string>()
                : (JsonSerializer.Deserialize<string[]>(value, SerializerOptions) ?? Array.Empty<string>()));

        var filtersComparer = new ValueComparer<string[]>(
            (left, right) => SequenceEquals(left, right),
            value => ComputeHash(value),
            value => SnapshotFilters(value));

        var utcDateTimeConverter = new ValueConverter<DateTimeOffset, DateTime>(
            value => value.UtcDateTime,
            value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));

        modelBuilder.Entity<FrameArchiveEntity>(entity =>
        {
            entity.ToTable("image_frame_archive");

            entity.HasKey(e => e.FrameId);

            entity.Property(e => e.FrameId)
                .HasColumnName("frame_id")
                .ValueGeneratedNever();
            entity.Property(e => e.CapturedAtUtc)
                .HasColumnName("captured_at_utc")
                .HasConversion(utcDateTimeConverter);
            entity.Property(e => e.RigName).HasColumnName("rig_name");
            entity.Property(e => e.CameraName).HasColumnName("camera_name");
            entity.Property(e => e.FramesStacked).HasColumnName("frames_stacked");
            entity.Property(e => e.IntegrationMilliseconds).HasColumnName("integration_ms");
            entity.Property(e => e.AppliedFilters)
                .HasColumnName("applied_filters_json")
                .HasConversion(filtersConverter)
                .Metadata.SetValueComparer(filtersComparer);
            entity.Property(e => e.QueueLatencyMilliseconds).HasColumnName("queue_latency_ms");
            entity.Property(e => e.ProcessingMilliseconds).HasColumnName("processing_ms");
            entity.Property(e => e.FullPipelineMilliseconds).HasColumnName("full_pipeline_ms");
            entity.Property(e => e.PayloadContentType).HasColumnName("payload_content_type");
            entity.Property(e => e.PayloadExtension).HasColumnName("payload_extension");
            entity.Property(e => e.ThumbnailFilePath).HasColumnName("thumbnail_file_path");
            entity.Property(e => e.ThumbnailObjectKey).HasColumnName("thumbnail_object_key");
            entity.Property(e => e.ThumbnailBucket).HasColumnName("thumbnail_bucket");
            entity.Property(e => e.MediaFilePath).HasColumnName("media_file_path");
            entity.Property(e => e.MediaObjectKey).HasColumnName("media_object_key");
            entity.Property(e => e.MediaBucket).HasColumnName("media_bucket");
            entity.Property(e => e.RawMediaFilePath).HasColumnName("raw_media_file_path");
            entity.Property(e => e.RawMediaObjectKey).HasColumnName("raw_media_object_key");
            entity.Property(e => e.RawMediaBucket).HasColumnName("raw_media_bucket");
            entity.Property(e => e.ArchivedAtUtc)
                .HasColumnName("archived_at_utc")
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasConversion(utcDateTimeConverter);

            entity.HasIndex(e => e.CapturedAtUtc).HasDatabaseName("ix_image_frame_archive_captured_at");
            entity.HasIndex(e => e.RigName).HasDatabaseName("ix_image_frame_archive_rig_name");
            entity.HasIndex(e => e.CameraName).HasDatabaseName("ix_image_frame_archive_camera_name");
            entity.HasIndex(e => e.FramesStacked).HasDatabaseName("ix_image_frame_archive_frames_stacked");
        });
    }

        private static bool SequenceEquals(string[]? left, string[]? right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            for (var index = 0; index < left.Length; index++)
            {
                if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static int ComputeHash(string[]? value)
        {
            if (value == null || value.Length == 0)
            {
                return 0;
            }

            var hash = new HashCode();
            foreach (var entry in value)
            {
                hash.Add(entry, StringComparer.Ordinal);
            }

            return hash.ToHashCode();
        }

        private static string[] SnapshotFilters(string[]? value)
            => value == null ? Array.Empty<string>() : value.ToArray();
}
