using HVO.SkyMonitorV5.Data.Telemetry.Entities;
using Microsoft.EntityFrameworkCore;

namespace HVO.SkyMonitorV5.Data.Telemetry;

public sealed class SkyMonitorTelemetryContext : DbContext
{
    public SkyMonitorTelemetryContext(DbContextOptions<SkyMonitorTelemetryContext> options)
        : base(options)
    {
    }

    public DbSet<RemoteDispatchAttemptEntity> RemoteDispatchAttempts => Set<RemoteDispatchAttemptEntity>();

    public DbSet<FrameExportAttemptEntity> FrameExportAttempts => Set<FrameExportAttemptEntity>();

    public DbSet<FrameExportRetryEntity> FrameExportRetries => Set<FrameExportRetryEntity>();

    public DbSet<BackgroundStackerSampleEntity> BackgroundStackerSamples => Set<BackgroundStackerSampleEntity>();

    public DbSet<CapturePacingSampleEntity> CapturePacingSamples => Set<CapturePacingSampleEntity>();

    public DbSet<ProcessingQueueSampleEntity> ProcessingQueueSamples => Set<ProcessingQueueSampleEntity>();

    public DbSet<FilterMetricSampleEntity> FilterMetricSamples => Set<FilterMetricSampleEntity>();

    public DbSet<TelemetryEventEntity> TelemetryEvents => Set<TelemetryEventEntity>();

    public DbSet<TelemetrySystemProfileEntity> TelemetrySystemProfiles => Set<TelemetrySystemProfileEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RemoteDispatchAttemptEntity>(entity =>
        {
            entity.ToTable("remote_dispatch_attempt");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AttemptedAtUtc).HasColumnName("attempted_at_utc");
            entity.Property(e => e.AttemptedAtLocal).HasColumnName("attempted_at_local");
            entity.Property(e => e.Mode).HasColumnName("mode");
            entity.Property(e => e.Outcome).HasColumnName("outcome");
            entity.Property(e => e.LatencyMilliseconds).HasColumnName("latency_ms");
            entity.Property(e => e.PayloadBytes).HasColumnName("payload_bytes");
            entity.Property(e => e.PayloadContentType).HasColumnName("payload_content_type");
            entity.Property(e => e.PayloadExtension).HasColumnName("payload_extension");
            entity.Property(e => e.Message).HasColumnName("message");
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message");
            entity.Property(e => e.FormatKey).HasColumnName("format_key");

            entity.HasIndex(e => e.AttemptedAtLocal).HasDatabaseName("ix_remote_dispatch_attempt_local");
            entity.HasIndex(e => e.Outcome).HasDatabaseName("ix_remote_dispatch_attempt_outcome");
            entity.HasIndex(e => e.FormatKey).HasDatabaseName("ix_remote_dispatch_attempt_format");
        });

        modelBuilder.Entity<FrameExportAttemptEntity>(entity =>
        {
            entity.ToTable("frame_export_attempt");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AttemptedAtUtc).HasColumnName("attempted_at_utc");
            entity.Property(e => e.AttemptedAtLocal).HasColumnName("attempted_at_local");
            entity.Property(e => e.FrameId).HasColumnName("frame_id");
            entity.Property(e => e.Stage).HasColumnName("stage");
            entity.Property(e => e.SinkName).HasColumnName("sink_name");
            entity.Property(e => e.Success).HasColumnName("success");
            entity.Property(e => e.LatencyMilliseconds).HasColumnName("latency_ms");
            entity.Property(e => e.PayloadBytes).HasColumnName("payload_bytes");
            entity.Property(e => e.PayloadContentType).HasColumnName("payload_content_type");
            entity.Property(e => e.PayloadExtension).HasColumnName("payload_extension");
            entity.Property(e => e.QueueLatencyMilliseconds).HasColumnName("queue_latency_ms");
            entity.Property(e => e.ProcessingMilliseconds).HasColumnName("processing_ms");
            entity.Property(e => e.FramesStacked).HasColumnName("frames_stacked");
            entity.Property(e => e.IntegrationMilliseconds).HasColumnName("integration_ms");
            entity.Property(e => e.FullPipelineMilliseconds).HasColumnName("full_pipeline_ms");
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message");

            entity.HasIndex(e => e.AttemptedAtLocal).HasDatabaseName("ix_frame_export_attempt_local");
            entity.HasIndex(e => new { e.Stage, e.SinkName }).HasDatabaseName("ix_frame_export_attempt_stage_sink");
            entity.HasIndex(e => e.FrameId).HasDatabaseName("ix_frame_export_attempt_frame");
        });

        modelBuilder.Entity<FrameExportRetryEntity>(entity =>
        {
            entity.ToTable("frame_export_retry");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.FrameId).HasColumnName("frame_id");
            entity.Property(e => e.Stage).HasColumnName("stage");
            entity.Property(e => e.SinkName).HasColumnName("sink_name");
            entity.Property(e => e.EnqueuedAtUtc).HasColumnName("enqueued_at_utc");
            entity.Property(e => e.NextAttemptAtUtc).HasColumnName("next_attempt_at_utc");
            entity.Property(e => e.LastAttemptAtUtc).HasColumnName("last_attempt_at_utc");
            entity.Property(e => e.AttemptCount).HasColumnName("attempt_count");
            entity.Property(e => e.Payload).HasColumnName("payload");
            entity.Property(e => e.ContentType).HasColumnName("content_type");
            entity.Property(e => e.FileExtension).HasColumnName("file_extension");
            entity.Property(e => e.MetadataJson).HasColumnName("metadata_json");
            entity.Property(e => e.LastErrorMessage).HasColumnName("last_error_message");

            entity.HasIndex(e => e.NextAttemptAtUtc).HasDatabaseName("ix_frame_export_retry_next_attempt");
            entity.HasIndex(e => new { e.Stage, e.SinkName }).HasDatabaseName("ix_frame_export_retry_stage_sink");
        });

        modelBuilder.Entity<BackgroundStackerSampleEntity>(entity =>
        {
            entity.ToTable("background_stacker_sample");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CapturedAtUtc).HasColumnName("captured_at_utc");
            entity.Property(e => e.CapturedAtLocal).HasColumnName("captured_at_local");
            entity.Property(e => e.QueueFillPercentage).HasColumnName("queue_fill_percentage");
            entity.Property(e => e.QueueDepth).HasColumnName("queue_depth");
            entity.Property(e => e.QueueCapacity).HasColumnName("queue_capacity");
            entity.Property(e => e.QueueLatencyMilliseconds).HasColumnName("queue_latency_ms");
            entity.Property(e => e.StackDurationMilliseconds).HasColumnName("stack_duration_ms");
            entity.Property(e => e.FilterDurationMilliseconds).HasColumnName("filter_duration_ms");
            entity.Property(e => e.QueuePressureLevel).HasColumnName("queue_pressure_level");
            entity.Property(e => e.SecondsSinceLastCompleted).HasColumnName("seconds_since_last_completed");
            entity.Property(e => e.QueueMemoryMegabytes).HasColumnName("queue_memory_mb");

            entity.HasIndex(e => e.CapturedAtLocal).HasDatabaseName("ix_background_stacker_sample_local");
        });

        modelBuilder.Entity<CapturePacingSampleEntity>(entity =>
        {
            entity.ToTable("capture_pacing_sample");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CapturedAtUtc).HasColumnName("captured_at_utc");
            entity.Property(e => e.CapturedAtLocal).HasColumnName("captured_at_local");
            entity.Property(e => e.Enabled).HasColumnName("enabled");
            entity.Property(e => e.UsingBackgroundStacker).HasColumnName("using_background_stacker");
            entity.Property(e => e.BaseDelayMilliseconds).HasColumnName("base_delay_ms");
            entity.Property(e => e.AdjustedDelayMilliseconds).HasColumnName("adjusted_delay_ms");
            entity.Property(e => e.QueuePressureLevel).HasColumnName("queue_pressure_level");
            entity.Property(e => e.PressureAdditionalDelayMilliseconds).HasColumnName("pressure_delay_ms");
            entity.Property(e => e.PenaltyAdditionalDelayMilliseconds).HasColumnName("penalty_delay_ms");
            entity.Property(e => e.PenaltyActive).HasColumnName("penalty_active");
            entity.Property(e => e.PenaltyExpiresAtLocal).HasColumnName("penalty_expires_at_local");

            entity.HasIndex(e => e.CapturedAtLocal).HasDatabaseName("ix_capture_pacing_sample_local");
        });

        modelBuilder.Entity<ProcessingQueueSampleEntity>(entity =>
        {
            entity.ToTable("processing_queue_sample");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CapturedAtUtc).HasColumnName("captured_at_utc");
            entity.Property(e => e.CapturedAtLocal).HasColumnName("captured_at_local");
            entity.Property(e => e.Enabled).HasColumnName("enabled");
            entity.Property(e => e.Capacity).HasColumnName("capacity");
            entity.Property(e => e.Depth).HasColumnName("depth");
            entity.Property(e => e.BackpressureEvents).HasColumnName("backpressure_events");
            entity.Property(e => e.LastEnqueueWaitMilliseconds).HasColumnName("last_enqueue_wait_ms");
            entity.Property(e => e.PeakEnqueueWaitMilliseconds).HasColumnName("peak_enqueue_wait_ms");
            entity.Property(e => e.AverageEnqueueWaitMilliseconds).HasColumnName("avg_enqueue_wait_ms");
            entity.Property(e => e.LastProcessingMilliseconds).HasColumnName("last_processing_ms");
            entity.Property(e => e.PeakProcessingMilliseconds).HasColumnName("peak_processing_ms");
            entity.Property(e => e.AverageProcessingMilliseconds).HasColumnName("avg_processing_ms");

            entity.HasIndex(e => e.CapturedAtLocal).HasDatabaseName("ix_processing_queue_sample_local");
        });

        modelBuilder.Entity<FilterMetricSampleEntity>(entity =>
        {
            entity.ToTable("filter_metric_sample");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CapturedAtUtc).HasColumnName("captured_at_utc");
            entity.Property(e => e.CapturedAtLocal).HasColumnName("captured_at_local");
            entity.Property(e => e.FilterName).HasColumnName("filter_name");
            entity.Property(e => e.AppliedCount).HasColumnName("applied_count");
            entity.Property(e => e.LastDurationMilliseconds).HasColumnName("last_duration_ms");
            entity.Property(e => e.AverageDurationMilliseconds).HasColumnName("average_duration_ms");

            entity.HasIndex(e => new { e.FilterName, e.CapturedAtLocal }).HasDatabaseName("ix_filter_metric_sample_filter_time");
        });

        modelBuilder.Entity<TelemetryEventEntity>(entity =>
        {
            entity.ToTable("telemetry_event");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.OccurredAtUtc).HasColumnName("occurred_at_utc");
            entity.Property(e => e.OccurredAtLocal).HasColumnName("occurred_at_local");
            entity.Property(e => e.Category).HasColumnName("category");
            entity.Property(e => e.EventType).HasColumnName("event_type");
            entity.Property(e => e.Severity).HasColumnName("severity");
            entity.Property(e => e.Summary).HasColumnName("summary");
            entity.Property(e => e.Detail).HasColumnName("detail");
            entity.Property(e => e.PropertiesJson).HasColumnName("properties_json");

            entity.HasIndex(e => e.OccurredAtLocal).HasDatabaseName("ix_telemetry_event_local");
            entity.HasIndex(e => e.Category).HasDatabaseName("ix_telemetry_event_category");
            entity.HasIndex(e => e.EventType).HasDatabaseName("ix_telemetry_event_type");
        });

        modelBuilder.Entity<TelemetrySystemProfileEntity>(entity =>
        {
            entity.ToTable("telemetry_system_profile");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.SystemHash).IsRequired().HasMaxLength(128).HasColumnName("system_hash");
            entity.Property(e => e.MachineName).HasMaxLength(256).HasColumnName("machine_name");
            entity.Property(e => e.HostName).HasMaxLength(256).HasColumnName("host_name");
            entity.Property(e => e.OperatingSystem).HasMaxLength(256).HasColumnName("operating_system");
            entity.Property(e => e.OsArchitecture).HasMaxLength(64).HasColumnName("os_architecture");
            entity.Property(e => e.ProcessArchitecture).HasMaxLength(64).HasColumnName("process_architecture");
            entity.Property(e => e.FrameworkDescription).HasMaxLength(128).HasColumnName("framework_description");
            entity.Property(e => e.ProcessorCount).HasColumnName("processor_count");
            entity.Property(e => e.TotalMemoryMegabytes).HasColumnName("total_memory_mb");
            entity.Property(e => e.CpuModel).HasMaxLength(256).HasColumnName("cpu_model");
            entity.Property(e => e.HardwareModel).HasMaxLength(256).HasColumnName("hardware_model");
            entity.Property(e => e.IsContainerized).HasColumnName("is_containerized");
            entity.Property(e => e.AdditionalPropertiesJson).HasColumnName("additional_properties_json");
            entity.Property(e => e.FirstSeenAtUtc).IsRequired().HasColumnName("first_seen_at_utc");
            entity.Property(e => e.LastSeenAtUtc).IsRequired().HasColumnName("last_seen_at_utc");

            entity.HasIndex(e => e.SystemHash).IsUnique().HasDatabaseName("ux_telemetry_system_profile_hash");
            entity.HasIndex(e => e.LastSeenAtUtc).HasDatabaseName("ix_telemetry_system_profile_last_seen");
        });
    }
}
