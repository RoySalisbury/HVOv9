using System.Text.Json;
using HVO.SkyMonitorV5.Data.Configurations.Entities;
using Microsoft.EntityFrameworkCore;

namespace HVO.SkyMonitorV5.Data.Configurations;

public sealed class SkyMonitorConfigurationContext : DbContext
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General);

    public SkyMonitorConfigurationContext(DbContextOptions<SkyMonitorConfigurationContext> options)
        : base(options)
    {
    }

    public DbSet<ObservatorySiteEntity> ObservatorySites => Set<ObservatorySiteEntity>();
    public DbSet<CameraCatalogCameraEntity> CameraCatalogCameras => Set<CameraCatalogCameraEntity>();
    public DbSet<CameraCatalogLensEntity> CameraCatalogLenses => Set<CameraCatalogLensEntity>();
    public DbSet<RigCatalogEntryEntity> RigCatalogEntries => Set<RigCatalogEntryEntity>();
    public DbSet<CameraAdapterConfigEntity> CameraAdapters => Set<CameraAdapterConfigEntity>();
    public DbSet<CameraPipelineConfigEntity> CameraPipelineConfigurations => Set<CameraPipelineConfigEntity>();
    public DbSet<StarCatalogSettingsEntity> StarCatalogSettings => Set<StarCatalogSettingsEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureObservatorySiteEntity(modelBuilder);
        ConfigureCameraCatalogCameraEntity(modelBuilder);
        ConfigureCameraCatalogLensEntity(modelBuilder);
        ConfigureRigCatalogEntity(modelBuilder);
        ConfigureCameraAdapterEntity(modelBuilder);
        ConfigureCameraPipelineEntity(modelBuilder);
        ConfigureStarCatalogSettingsEntity(modelBuilder);
    }

    private static void ConfigureObservatorySiteEntity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ObservatorySiteEntity>(entity =>
        {
            entity.ToTable("observatory_site");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Slug).HasColumnName("slug");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.LatitudeDegrees).HasColumnName("latitude_degrees");
            entity.Property(e => e.LongitudeDegrees).HasColumnName("longitude_degrees");
            entity.Property(e => e.TimeZoneId).HasColumnName("time_zone_id");

            entity.HasIndex(e => e.Slug).IsUnique();

            entity.HasData(new ObservatorySiteEntity
            {
                Id = 1,
                Slug = "hvo-primary",
                Name = "Hualapai Valley Observatory",
                LatitudeDegrees = 35.347,
                LongitudeDegrees = -113.878,
                TimeZoneId = "America/Phoenix"
            });
        });
    }

    private static void ConfigureCameraCatalogCameraEntity(ModelBuilder modelBuilder)
    {
        var additionalTags = JsonSerializer.Serialize(new[] { "Simulation" }, SerializerOptions);

        modelBuilder.Entity<CameraCatalogCameraEntity>(entity =>
        {
            entity.ToTable("camera_catalog_camera");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Key).HasColumnName("key");
            entity.Property(e => e.DisplayName).HasColumnName("display_name");
            entity.Property(e => e.Manufacturer).HasColumnName("manufacturer");
            entity.Property(e => e.Model).HasColumnName("model");
            entity.Property(e => e.DriverVersion).HasColumnName("driver_version");
            entity.Property(e => e.AdapterName).HasColumnName("adapter_name");
            entity.Property(e => e.DriverId).HasColumnName("driver_id");
            entity.Property(e => e.IsSynthetic).HasColumnName("is_synthetic");
            entity.Property(e => e.SyntheticProfile).HasColumnName("synthetic_profile");
            entity.Property(e => e.SensorWidthPixels).HasColumnName("sensor_width_px");
            entity.Property(e => e.SensorHeightPixels).HasColumnName("sensor_height_px");
            entity.Property(e => e.PixelSizeMicrons).HasColumnName("pixel_size_microns");
            entity.Property(e => e.SensorCxPixels).HasColumnName("sensor_cx_px");
            entity.Property(e => e.SensorCyPixels).HasColumnName("sensor_cy_px");
            entity.Property(e => e.ColorMode).HasColumnName("color_mode");
            entity.Property(e => e.SensorTechnology).HasColumnName("sensor_technology");
            entity.Property(e => e.BodyType).HasColumnName("body_type");
            entity.Property(e => e.Cooling).HasColumnName("cooling");
            entity.Property(e => e.SupportsGainControl).HasColumnName("supports_gain_control");
            entity.Property(e => e.SupportsExposureControl).HasColumnName("supports_exposure_control");
            entity.Property(e => e.SupportsTemperatureTelemetry).HasColumnName("supports_temperature_telemetry");
            entity.Property(e => e.SupportsSoftwareBinning).HasColumnName("supports_software_binning");
            entity.Property(e => e.AdditionalTagsJson).HasColumnName("additional_tags_json");

            entity.HasIndex(e => e.Key).IsUnique();

            entity.HasData(new CameraCatalogCameraEntity
            {
                Id = 1,
                Key = "MockASI174MM",
                DisplayName = "Mock ASI174MM",
                Manufacturer = "HVO",
                Model = "Mock Fisheye AllSky",
                DriverVersion = "2.0.0",
                AdapterName = "MockCameraAdapter",
                DriverId = "Synthetic",
                IsSynthetic = true,
                SyntheticProfile = "MockFisheye",
                SensorWidthPixels = 1936,
                SensorHeightPixels = 1216,
                PixelSizeMicrons = 5.86,
                ColorMode = "Monochrome",
                SensorTechnology = "Cmos",
                BodyType = "Synthetic",
                Cooling = "None",
                SupportsGainControl = true,
                SupportsExposureControl = true,
                SupportsTemperatureTelemetry = false,
                SupportsSoftwareBinning = true,
                AdditionalTagsJson = additionalTags
            });
        });
    }

    private static void ConfigureCameraCatalogLensEntity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CameraCatalogLensEntity>(entity =>
        {
            entity.ToTable("camera_catalog_lens");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Key).HasColumnName("key");
            entity.Property(e => e.DisplayName).HasColumnName("display_name");
            entity.Property(e => e.ProjectionModel).HasColumnName("projection_model");
            entity.Property(e => e.FocalLengthMillimeters).HasColumnName("focal_length_mm");
            entity.Property(e => e.FieldOfViewXDegrees).HasColumnName("fov_x_deg");
            entity.Property(e => e.FieldOfViewYDegrees).HasColumnName("fov_y_deg");
            entity.Property(e => e.RollDegrees).HasColumnName("roll_deg");
            entity.Property(e => e.Kind).HasColumnName("kind");

            entity.HasIndex(e => e.Key).IsUnique();

            entity.HasData(new CameraCatalogLensEntity
            {
                Id = 1,
                Key = "Fujinon_FE185C086HA_1",
                DisplayName = "Fujinon FE185C086HA-1",
                ProjectionModel = "Equidistant",
                FocalLengthMillimeters = 2.7,
                FieldOfViewXDegrees = 185.0,
                FieldOfViewYDegrees = 185.0,
                RollDegrees = 0.0,
                Kind = "Fisheye"
            });
        });
    }

    private static void ConfigureRigCatalogEntity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RigCatalogEntryEntity>(entity =>
        {
            entity.ToTable("rig_catalog_entry");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Key).HasColumnName("key");
            entity.Property(e => e.DisplayName).HasColumnName("display_name");
            entity.Property(e => e.CameraId).HasColumnName("camera_id");
            entity.Property(e => e.LensId).HasColumnName("lens_id");
            entity.Property(e => e.BoresightAltitudeDegrees).HasColumnName("boresight_alt_deg");
            entity.Property(e => e.BoresightAzimuthDegrees).HasColumnName("boresight_az_deg");
            entity.Property(e => e.IsActive).HasColumnName("is_active");

            entity.HasIndex(e => e.Key).IsUnique();

            entity.HasOne(e => e.Camera)
                .WithMany()
                .HasForeignKey(e => e.CameraId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Lens)
                .WithMany()
                .HasForeignKey(e => e.LensId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasData(new RigCatalogEntryEntity
            {
                Id = 1,
                Key = "MockFisheye",
                DisplayName = "Mock Fisheye",
                CameraId = 1,
                LensId = 1,
                BoresightAltitudeDegrees = 90.0,
                BoresightAzimuthDegrees = 0.0,
                IsActive = true
            });
        });
    }

    private static void ConfigureCameraAdapterEntity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CameraAdapterConfigEntity>(entity =>
        {
            entity.ToTable("camera_adapter_config");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.AdapterType).HasColumnName("adapter_type");
            entity.Property(e => e.RigId).HasColumnName("rig_id");

            entity.HasIndex(e => e.Name).IsUnique();

            entity.HasOne(e => e.Rig)
                .WithMany()
                .HasForeignKey(e => e.RigId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasData(new CameraAdapterConfigEntity
            {
                Id = 1,
                Name = "MockFisheye",
                AdapterType = "Mock",
                RigId = 1
            });
        });
    }

    private static void ConfigureCameraPipelineEntity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CameraPipelineConfigEntity>(entity =>
        {
            entity.ToTable("camera_pipeline_config");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.EnableStacking).HasColumnName("enable_stacking");
            entity.Property(e => e.EnableImageOverlays).HasColumnName("enable_image_overlays");
            entity.Property(e => e.CaptureIntervalMilliseconds).HasColumnName("capture_interval_ms");
            entity.Property(e => e.StackingFrameCount).HasColumnName("stacking_frame_count");
            entity.Property(e => e.StackingBufferMinimumFrames).HasColumnName("stacking_buffer_min_frames");
            entity.Property(e => e.StackingBufferIntegrationSeconds).HasColumnName("stacking_buffer_integration_seconds");
            entity.Property(e => e.DayExposureMilliseconds).HasColumnName("day_exposure_ms");
            entity.Property(e => e.NightExposureMilliseconds).HasColumnName("night_exposure_ms");
            entity.Property(e => e.DayGain).HasColumnName("day_gain");
            entity.Property(e => e.NightGain).HasColumnName("night_gain");
            entity.Property(e => e.DayStartExposureMilliseconds).HasColumnName("day_start_exposure_ms");
            entity.Property(e => e.NightStartExposureMilliseconds).HasColumnName("night_start_exposure_ms");
            entity.Property(e => e.DayMinimumExposureMilliseconds).HasColumnName("day_min_exposure_ms");
            entity.Property(e => e.DayMaximumExposureMilliseconds).HasColumnName("day_max_exposure_ms");
            entity.Property(e => e.NightMinimumExposureMilliseconds).HasColumnName("night_min_exposure_ms");
            entity.Property(e => e.NightMaximumExposureMilliseconds).HasColumnName("night_max_exposure_ms");
            entity.Property(e => e.DayStartGain).HasColumnName("day_start_gain");
            entity.Property(e => e.NightStartGain).HasColumnName("night_start_gain");
            entity.Property(e => e.DayMinimumGain).HasColumnName("day_min_gain");
            entity.Property(e => e.DayMaximumGain).HasColumnName("day_max_gain");
            entity.Property(e => e.NightMinimumGain).HasColumnName("night_min_gain");
            entity.Property(e => e.NightMaximumGain).HasColumnName("night_max_gain");
            entity.Property(e => e.DayNightTransitionHourOffset).HasColumnName("day_night_transition_hour_offset");
            entity.Property(e => e.OverlayTextFormat).HasColumnName("overlay_text_format");

            entity.HasData(new
            {
                Id = 1,
                Name = "Default",
                EnableStacking = true,
                EnableImageOverlays = true,
                CaptureIntervalMilliseconds = 1000,
                StackingFrameCount = 4,
                StackingBufferMinimumFrames = 24,
                StackingBufferIntegrationSeconds = 120,
                DayExposureMilliseconds = 50,
                NightExposureMilliseconds = 5000,
                DayGain = 50,
                NightGain = 200,
                DayStartExposureMilliseconds = 2000,
                NightStartExposureMilliseconds = 5000,
                DayMinimumExposureMilliseconds = 1,
                DayMaximumExposureMilliseconds = 60000,
                NightMinimumExposureMilliseconds = 1,
                NightMaximumExposureMilliseconds = 60000,
                DayStartGain = 50,
                NightStartGain = 200,
                DayMinimumGain = 0,
                DayMaximumGain = 500,
                NightMinimumGain = 0,
                NightMaximumGain = 500,
                DayNightTransitionHourOffset = 0,
                OverlayTextFormat = "yyyy-MM-dd HH:mm:ss zzz"
            });

            entity.OwnsOne(e => e.ProcessedImageEncoding, builder =>
            {
                builder.Property(p => p.Format).HasColumnName("processed_image_format");
                builder.Property(p => p.Quality).HasColumnName("processed_image_quality");

                builder.HasData(new
                {
                    CameraPipelineConfigEntityId = 1,
                    Format = "Jpeg",
                    Quality = 90
                });
            });

            entity.OwnsOne(e => e.BackgroundStacker, builder =>
            {
                builder.Property(p => p.Enabled).HasColumnName("bg_enabled");
                builder.Property(p => p.QueueCapacity).HasColumnName("bg_queue_capacity");
                builder.Property(p => p.OverflowPolicy).HasColumnName("bg_overflow_policy");
                builder.Property(p => p.CompressionMode).HasColumnName("bg_compression_mode");
                builder.Property(p => p.RestartDelaySeconds).HasColumnName("bg_restart_delay_seconds");

                builder.HasData(new
                {
                    CameraPipelineConfigEntityId = 1,
                    Enabled = true,
                    QueueCapacity = 32,
                    OverflowPolicy = "Block",
                    CompressionMode = "None",
                    RestartDelaySeconds = 5
                });

                builder.OwnsOne(p => p.AdaptiveQueue, adaptive =>
                {
                    adaptive.Property(a => a.Enabled).HasColumnName("bg_adaptive_enabled");
                    adaptive.Property(a => a.MinCapacity).HasColumnName("bg_adaptive_min_capacity");
                    adaptive.Property(a => a.MaxCapacity).HasColumnName("bg_adaptive_max_capacity");
                    adaptive.Property(a => a.IncreaseStep).HasColumnName("bg_adaptive_increase_step");
                    adaptive.Property(a => a.DecreaseStep).HasColumnName("bg_adaptive_decrease_step");
                    adaptive.Property(a => a.ScaleUpThresholdPercent).HasColumnName("bg_adaptive_scale_up_percent");
                    adaptive.Property(a => a.ScaleDownThresholdPercent).HasColumnName("bg_adaptive_scale_down_percent");
                    adaptive.Property(a => a.EvaluationWindowSeconds).HasColumnName("bg_adaptive_evaluation_window_seconds");
                    adaptive.Property(a => a.CooldownSeconds).HasColumnName("bg_adaptive_cooldown_seconds");

                    adaptive.HasData(new
                    {
                        BackgroundStackerSettingsCameraPipelineConfigEntityId = 1,
                        CameraPipelineConfigEntityId = 1,
                        Enabled = true,
                        MinCapacity = 24,
                        MaxCapacity = 48,
                        IncreaseStep = 4,
                        DecreaseStep = 4,
                        ScaleUpThresholdPercent = 75,
                        ScaleDownThresholdPercent = 35,
                        EvaluationWindowSeconds = 6,
                        CooldownSeconds = 30
                    });
                });
            });

            entity.OwnsOne(e => e.CapturePacing, builder =>
            {
                builder.Property(p => p.Enabled).HasColumnName("pacing_enabled");
                builder.Property(p => p.ElevatedAdditionalDelayMilliseconds).HasColumnName("pacing_elevated_delay_ms");
                builder.Property(p => p.HighAdditionalDelayMilliseconds).HasColumnName("pacing_high_delay_ms");
                builder.Property(p => p.CriticalAdditionalDelayMilliseconds).HasColumnName("pacing_critical_delay_ms");
                builder.Property(p => p.RejectionPenaltyMilliseconds).HasColumnName("pacing_rejection_penalty_ms");
                builder.Property(p => p.RejectionPenaltyDurationSeconds).HasColumnName("pacing_rejection_penalty_duration_seconds");
                builder.Property(p => p.RampUpStepMilliseconds).HasColumnName("pacing_ramp_up_step_ms");
                builder.Property(p => p.RampDownStepMilliseconds).HasColumnName("pacing_ramp_down_step_ms");
                builder.Property(p => p.MaxDelayMilliseconds).HasColumnName("pacing_max_delay_ms");

                builder.HasData(new
                {
                    CameraPipelineConfigEntityId = 1,
                    Enabled = true,
                    ElevatedAdditionalDelayMilliseconds = 250,
                    HighAdditionalDelayMilliseconds = 500,
                    CriticalAdditionalDelayMilliseconds = 1000,
                    RejectionPenaltyMilliseconds = 2000,
                    RejectionPenaltyDurationSeconds = 12,
                    RampUpStepMilliseconds = 150,
                    RampDownStepMilliseconds = 300,
                    MaxDelayMilliseconds = 6000
                });
            });

            entity.OwnsOne(e => e.RemoteDispatch, builder =>
            {
                builder.Property(p => p.Enabled).HasColumnName("dispatch_enabled");
                builder.Property(p => p.Mode).HasColumnName("dispatch_mode");
                builder.Property(p => p.S3Bucket).HasColumnName("dispatch_s3_bucket");
                builder.Property(p => p.FanoutExchange).HasColumnName("dispatch_fanout_exchange");
                builder.Property(p => p.Region).HasColumnName("dispatch_region");

                builder.HasData(new
                {
                    CameraPipelineConfigEntityId = 1,
                    Enabled = false,
                    Mode = "None",
                    S3Bucket = (string?)null,
                    FanoutExchange = (string?)null,
                    Region = "us-west-2"
                });
            });

            entity.OwnsOne(e => e.CardinalDirections, builder =>
            {
                builder.Property(p => p.OffsetXPixels).HasColumnName("cardinal_offset_x");
                builder.Property(p => p.OffsetYPixels).HasColumnName("cardinal_offset_y");
                builder.Property(p => p.RotationDegrees).HasColumnName("cardinal_rotation_deg");
                builder.Property(p => p.RadiusOffsetPixels).HasColumnName("cardinal_radius_offset_px");
                builder.Property(p => p.LabelNorth).HasColumnName("cardinal_label_north");
                builder.Property(p => p.LabelSouth).HasColumnName("cardinal_label_south");
                builder.Property(p => p.LabelEast).HasColumnName("cardinal_label_east");
                builder.Property(p => p.LabelWest).HasColumnName("cardinal_label_west");
                builder.Property(p => p.SwapEastWest).HasColumnName("cardinal_swap_east_west");
                builder.Property(p => p.CircleColor).HasColumnName("cardinal_circle_color");
                builder.Property(p => p.CircleOpacity).HasColumnName("cardinal_circle_opacity");
                builder.Property(p => p.CircleThickness).HasColumnName("cardinal_circle_thickness");
                builder.Property(p => p.CircleLineStyle).HasColumnName("cardinal_circle_line_style");
                builder.Property(p => p.LabelFillOpacity).HasColumnName("cardinal_label_fill_opacity");
                builder.Property(p => p.LabelPadding).HasColumnName("cardinal_label_padding");
                builder.Property(p => p.LabelCornerRadius).HasColumnName("cardinal_label_corner_radius");
                builder.Property(p => p.LabelFontSize).HasColumnName("cardinal_label_font_size");

                builder.HasData(new
                {
                    CameraPipelineConfigEntityId = 1,
                    OffsetXPixels = 0,
                    OffsetYPixels = 0,
                    RotationDegrees = 0,
                    RadiusOffsetPixels = -35,
                    LabelNorth = "N",
                    LabelSouth = "S",
                    LabelEast = "E",
                    LabelWest = "W",
                    SwapEastWest = true,
                    CircleColor = "#C8D2E6",
                    CircleOpacity = 170,
                    CircleThickness = 1,
                    CircleLineStyle = "LongDash",
                    LabelFillOpacity = 220,
                    LabelPadding = 6,
                    LabelCornerRadius = 6,
                    LabelFontSize = 18
                });
            });

            entity.OwnsOne(e => e.CircularApertureMask, builder =>
            {
                builder.Property(p => p.OffsetXPixels).HasColumnName("mask_offset_x");
                builder.Property(p => p.OffsetYPixels).HasColumnName("mask_offset_y");
                builder.Property(p => p.RadiusOffsetPixels).HasColumnName("mask_radius_offset_px");
                builder.Property(p => p.MaskColor).HasColumnName("mask_color");
                builder.Property(p => p.MaskOpacity).HasColumnName("mask_opacity");

                builder.HasData(new
                {
                    CameraPipelineConfigEntityId = 1,
                    OffsetXPixels = 0,
                    OffsetYPixels = 0,
                    RadiusOffsetPixels = -4,
                    MaskColor = "#000000",
                    MaskOpacity = 220
                });
            });

            entity.OwnsOne(e => e.ConstellationFigures, builder =>
            {
                builder.Property(p => p.LineThickness).HasColumnName("constellation_line_thickness");
                builder.Property(p => p.LineOpacity).HasColumnName("constellation_line_opacity");
                builder.Property(p => p.LineColor).HasColumnName("constellation_line_color");
                builder.Property(p => p.UseDashedLine).HasColumnName("constellation_use_dashed_line");

                builder.HasData(new
                {
                    CameraPipelineConfigEntityId = 1,
                    LineThickness = 0.8,
                    LineOpacity = 0.4,
                    LineColor = "#7FB2FF",
                    UseDashedLine = true
                });
            });

            entity.OwnsOne(e => e.CelestialAnnotations, builder =>
            {
                builder.Property(p => p.LabelFontSize).HasColumnName("celestial_label_font_size");
                builder.Property(p => p.StarLabelColor).HasColumnName("celestial_star_label_color");
                builder.Property(p => p.PlanetLabelColor).HasColumnName("celestial_planet_label_color");
                builder.Property(p => p.DeepSkyLabelColor).HasColumnName("celestial_deep_sky_label_color");
                builder.Property(p => p.StarRingRadius).HasColumnName("celestial_star_ring_radius");
                builder.Property(p => p.PlanetRingRadius).HasColumnName("celestial_planet_ring_radius");
                builder.Property(p => p.DeepSkyRingRadius).HasColumnName("celestial_deep_sky_ring_radius");
                builder.Property(p => p.UseAutomaticStarSelection).HasColumnName("celestial_use_auto_star_selection");
                builder.Property(p => p.AutoStarCount).HasColumnName("celestial_auto_star_count");
                builder.Property(p => p.AutoStarMagnitudeLimit).HasColumnName("celestial_auto_star_magnitude_limit");
                builder.Property(p => p.AnnotatePlanets).HasColumnName("celestial_annotate_planets");

                builder.HasData(new
                {
                    CameraPipelineConfigEntityId = 1,
                    LabelFontSize = 12.0,
                    StarLabelColor = "#EBF5FF",
                    PlanetLabelColor = "#FFE8C5",
                    DeepSkyLabelColor = "#F0E4FF",
                    StarRingRadius = 6.0,
                    PlanetRingRadius = 10.0,
                    DeepSkyRingRadius = 12.0,
                    UseAutomaticStarSelection = true,
                    AutoStarCount = 30,
                    AutoStarMagnitudeLimit = 3.0,
                    AnnotatePlanets = true
                });

                builder.OwnsMany(p => p.DeepSkyObjects, dsb =>
                {
                    dsb.ToTable("celestial_annotation_deep_sky_object");
                    dsb.WithOwner().HasForeignKey("pipeline_id");
                    dsb.Property<int>("pipeline_id").HasColumnName("pipeline_id");

                    dsb.HasKey(e => e.Id);

                    dsb.Property(e => e.Id).HasColumnName("id");
                    dsb.Property(e => e.Name).HasColumnName("name");
                    dsb.Property(e => e.RightAscensionHours).HasColumnName("right_ascension_hours");
                    dsb.Property(e => e.DeclinationDegrees).HasColumnName("declination_degrees");
                    dsb.Property(e => e.Magnitude).HasColumnName("magnitude");
                    dsb.Property(e => e.Color).HasColumnName("color");

                    dsb.HasData(
                        new
                        {
                            Id = 1,
                            pipeline_id = 1,
                            Name = "M31 (Andromeda Galaxy)",
                            RightAscensionHours = 0.712,
                            DeclinationDegrees = 41.269,
                            Magnitude = 3.4,
                            Color = "#8FB7FF"
                        },
                        new
                        {
                            Id = 2,
                            pipeline_id = 1,
                            Name = "M13 (Great Globular Cluster)",
                            RightAscensionHours = 16.695,
                            DeclinationDegrees = 36.467,
                            Magnitude = 5.8,
                            Color = "#C6A7FF"
                        });
                });
            });

            entity.OwnsMany(e => e.Filters, builder =>
            {
                builder.ToTable("camera_pipeline_filter");
                builder.WithOwner().HasForeignKey("pipeline_id");
                builder.Property<int>("pipeline_id").HasColumnName("pipeline_id");

                builder.HasKey(e => e.Id);

                builder.Property(e => e.Id).HasColumnName("id");
                builder.Property(e => e.Name).HasColumnName("name");
                builder.Property(e => e.DisplayOrder).HasColumnName("display_order");
                builder.Property(e => e.Enabled).HasColumnName("enabled");

                builder.HasData(
                    new { Id = 1, pipeline_id = 1, Name = "CardinalDirections", DisplayOrder = 1, Enabled = true },
                    new { Id = 2, pipeline_id = 1, Name = "ConstellationFigures", DisplayOrder = 2, Enabled = true },
                    new { Id = 3, pipeline_id = 1, Name = "CelestialAnnotations", DisplayOrder = 3, Enabled = true },
                    new { Id = 4, pipeline_id = 1, Name = "OverlayText", DisplayOrder = 4, Enabled = false },
                    new { Id = 5, pipeline_id = 1, Name = "CircularApertureMask", DisplayOrder = 5, Enabled = true }
                );
            });
        });
    }

    private static void ConfigureStarCatalogSettingsEntity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StarCatalogSettingsEntity>(entity =>
        {
            entity.ToTable("star_catalog_settings");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.MagnitudeLimit).HasColumnName("magnitude_limit");
            entity.Property(e => e.MinMaxAltitudeDegrees).HasColumnName("min_max_altitude_degrees");
            entity.Property(e => e.TopStarCount).HasColumnName("top_star_count");
            entity.Property(e => e.StratifiedSelection).HasColumnName("stratified_selection");
            entity.Property(e => e.IncludePlanets).HasColumnName("include_planets");
            entity.Property(e => e.IncludeMoon).HasColumnName("include_moon");
            entity.Property(e => e.IncludeOuterPlanets).HasColumnName("include_outer_planets");
            entity.Property(e => e.IncludeSun).HasColumnName("include_sun");
            entity.Property(e => e.RightAscensionBins).HasColumnName("right_ascension_bins");
            entity.Property(e => e.DeclinationBands).HasColumnName("declination_bands");

            entity.HasData(new StarCatalogSettingsEntity
            {
                Id = 1,
                MagnitudeLimit = 6.5,
                MinMaxAltitudeDegrees = 10.0,
                TopStarCount = 500,
                StratifiedSelection = false,
                IncludePlanets = true,
                IncludeMoon = true,
                IncludeOuterPlanets = true,
                IncludeSun = false,
                RightAscensionBins = 24,
                DeclinationBands = 8
            });
        });
    }
}
