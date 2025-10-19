using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using HVO.SkyMonitorV5.Data.Configurations;
using HVO.SkyMonitorV5.Data.Configurations.Entities;
using HVO.SkyMonitorV5.RPi.Cameras.Drivers;
using HVO.SkyMonitorV5.RPi.Cameras.Optics;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Infrastructure;

/// <summary>
/// Loads SkyMonitor runtime option objects from the configuration SQLite database.
/// </summary>
public sealed class DatabaseBackedConfigurationOptionsConfigurator :
    IConfigureOptions<ObservatoryLocationOptions>,
    IConfigureOptions<AllSkyCatalogOptions>,
    IConfigureOptions<CameraPipelineOptions>,
    IConfigureOptions<CardinalDirectionsOptions>,
    IConfigureOptions<CircularApertureMaskOptions>,
    IConfigureOptions<CelestialAnnotationsOptions>,
    IConfigureOptions<ConstellationFigureOptions>,
    IConfigureOptions<StarCatalogOptions>,
    IConfigureOptions<LocalApiClientOptions>,
    IConfigureOptions<SkyMonitorTelemetryRetentionOptions>,
    IConfigurationSnapshotInvalidator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);

    private readonly IDbContextFactory<SkyMonitorConfigurationContext> _contextFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DatabaseBackedConfigurationOptionsConfigurator>? _logger;

    private readonly object _snapshotLock = new();
    private ConfigurationSnapshot? _snapshot;

    public DatabaseBackedConfigurationOptionsConfigurator(
        IDbContextFactory<SkyMonitorConfigurationContext> contextFactory,
        IConfiguration configuration,
        ILogger<DatabaseBackedConfigurationOptionsConfigurator>? logger = null)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger;
    }

    public void Configure(ObservatoryLocationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var snapshot = GetSnapshot();

        options.LatitudeDegrees = snapshot.Observatory.LatitudeDegrees;
        options.LongitudeDegrees = snapshot.Observatory.LongitudeDegrees;
        options.TimeZoneId = snapshot.Observatory.TimeZoneId;
    }

    public void Configure(LocalApiClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var snapshot = GetSnapshot();
        if (!snapshot.SystemSettings.TryGetValue(SystemSettingKeys.LocalApi, out var payload)
            || string.IsNullOrWhiteSpace(payload))
        {
            return;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<LocalApiClientOptions>(payload, JsonOptions);
            if (stored is null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(stored.BaseAddress))
            {
                options.BaseAddress = stored.BaseAddress;
            }

            options.ApiKey = stored.ApiKey;

            if (!string.IsNullOrWhiteSpace(stored.ApiKeyHeaderName))
            {
                options.ApiKeyHeaderName = stored.ApiKeyHeaderName;
            }

            if (stored.Timeout > TimeSpan.Zero)
            {
                options.Timeout = stored.Timeout;
            }
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "Unable to deserialize local API configuration from system settings.");
        }
    }

    public void Configure(SkyMonitorTelemetryRetentionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var snapshot = GetSnapshot();
        if (!snapshot.SystemSettings.TryGetValue(SystemSettingKeys.TelemetryRetention, out var payload)
            || string.IsNullOrWhiteSpace(payload))
        {
            return;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<SkyMonitorTelemetryRetentionOptions>(payload, JsonOptions);
            if (stored is null)
            {
                return;
            }

            if (stored.SweepInterval > TimeSpan.Zero)
            {
                options.SweepInterval = stored.SweepInterval;
            }

            options.VacuumAfterPurge = stored.VacuumAfterPurge;

            options.RemoteDispatch = ClonePolicy(stored.RemoteDispatch, options.RemoteDispatch);
            options.FrameExports = ClonePolicy(stored.FrameExports, options.FrameExports);
            options.BackgroundStacker = ClonePolicy(stored.BackgroundStacker, options.BackgroundStacker);
            options.CapturePacing = ClonePolicy(stored.CapturePacing, options.CapturePacing);
            options.ProcessingQueue = ClonePolicy(stored.ProcessingQueue, options.ProcessingQueue);
            options.FilterMetrics = ClonePolicy(stored.FilterMetrics, options.FilterMetrics);
            options.TelemetryEvents = ClonePolicy(stored.TelemetryEvents, options.TelemetryEvents);
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "Unable to deserialize telemetry retention configuration from system settings.");
        }
    }

    public void Configure(AllSkyCatalogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var snapshot = GetSnapshot();

        var cameraLookup = snapshot.Cameras.ToDictionary(c => c.Id);
    var lensLookup = snapshot.Optics.ToDictionary(l => l.Id);

        options.Cameras = snapshot.Cameras
            .Select(CreateCameraOption)
            .ToList();

    options.Lenses = snapshot.Optics
            .Select(CreateLensOption)
            .ToList();

        var rigEntries = new List<RigCatalogEntryOptions>();
        string activeRig = string.Empty;

        foreach (var rig in snapshot.Rigs)
        {
            if (!cameraLookup.TryGetValue(rig.CameraId, out var camera))
            {
                _logger?.LogWarning("Rig {RigKey} references unknown camera id {CameraId}.", rig.Key, rig.CameraId);
                continue;
            }

            if (!lensLookup.TryGetValue(rig.LensId, out var lens))
            {
                _logger?.LogWarning("Rig {RigKey} references unknown lens id {LensId}.", rig.Key, rig.LensId);
                continue;
            }

            rigEntries.Add(new RigCatalogEntryOptions
            {
                Name = rig.Key,
                DisplayName = rig.DisplayName,
                Camera = camera.Key,
                Lens = lens.Key,
                BoresightAltDeg = rig.BoresightAltitudeDegrees,
                BoresightAzDeg = rig.BoresightAzimuthDegrees
            });

            if (rig.IsActive && string.IsNullOrWhiteSpace(activeRig))
            {
                activeRig = rig.Key;
            }
        }

        if (string.IsNullOrWhiteSpace(activeRig) && rigEntries.Count > 0)
        {
            activeRig = rigEntries[0].Name;
        }

        options.Rigs = new RigCatalogOptions
        {
            ActiveRig = activeRig,
            Entries = rigEntries
        };
    }

    public void Configure(CameraPipelineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var snapshot = GetSnapshot();
        var pipeline = snapshot.Pipeline;

        options.CaptureIntervalMilliseconds = pipeline.CaptureIntervalMilliseconds;
        options.StackingFrameCount = pipeline.StackingFrameCount;
        options.StackingBufferMinimumFrames = pipeline.StackingBufferMinimumFrames;
        options.StackingBufferIntegrationSeconds = pipeline.StackingBufferIntegrationSeconds;
        options.EnableStacking = pipeline.EnableStacking;
        options.EnableImageOverlays = pipeline.EnableImageOverlays;
        options.DayNightTransitionHourOffset = pipeline.DayNightTransitionHourOffset;
        options.OverlayTextFormat = pipeline.OverlayTextFormat;

        ApplyExposureProfiles(options, pipeline);
        ApplyGainProfiles(options, pipeline);

        options.ProcessedImageEncoding = new ImageEncodingOptions
        {
            Format = ParseEnum(pipeline.ProcessedImageEncoding.Format, ImageEncodingFormat.Jpeg),
            Quality = Math.Clamp(pipeline.ProcessedImageEncoding.Quality, 1, 100)
        };

        options.BackgroundStacker = new BackgroundStackerOptions
        {
            Enabled = pipeline.BackgroundStacker.Enabled,
            QueueCapacity = pipeline.BackgroundStacker.QueueCapacity,
            OverflowPolicy = ParseEnum(pipeline.BackgroundStacker.OverflowPolicy, BackgroundStackerOverflowPolicy.Block),
            CompressionMode = ParseEnum(pipeline.BackgroundStacker.CompressionMode, BackgroundStackerCompressionMode.None),
            RestartDelaySeconds = pipeline.BackgroundStacker.RestartDelaySeconds,
            AdaptiveQueue = new AdaptiveQueueOptions
            {
                Enabled = pipeline.BackgroundStacker.AdaptiveQueue.Enabled,
                MinCapacity = pipeline.BackgroundStacker.AdaptiveQueue.MinCapacity,
                MaxCapacity = pipeline.BackgroundStacker.AdaptiveQueue.MaxCapacity,
                IncreaseStep = pipeline.BackgroundStacker.AdaptiveQueue.IncreaseStep,
                DecreaseStep = pipeline.BackgroundStacker.AdaptiveQueue.DecreaseStep,
                ScaleUpThresholdPercent = pipeline.BackgroundStacker.AdaptiveQueue.ScaleUpThresholdPercent,
                ScaleDownThresholdPercent = pipeline.BackgroundStacker.AdaptiveQueue.ScaleDownThresholdPercent,
                EvaluationWindowSeconds = pipeline.BackgroundStacker.AdaptiveQueue.EvaluationWindowSeconds,
                CooldownSeconds = pipeline.BackgroundStacker.AdaptiveQueue.CooldownSeconds
            }
        };

        options.CapturePacing = new CapturePacingOptions
        {
            Enabled = pipeline.CapturePacing.Enabled,
            ElevatedAdditionalDelayMilliseconds = pipeline.CapturePacing.ElevatedAdditionalDelayMilliseconds,
            HighAdditionalDelayMilliseconds = pipeline.CapturePacing.HighAdditionalDelayMilliseconds,
            CriticalAdditionalDelayMilliseconds = pipeline.CapturePacing.CriticalAdditionalDelayMilliseconds,
            RejectionPenaltyMilliseconds = pipeline.CapturePacing.RejectionPenaltyMilliseconds,
            RejectionPenaltyDurationSeconds = pipeline.CapturePacing.RejectionPenaltyDurationSeconds,
            RampUpStepMilliseconds = pipeline.CapturePacing.RampUpStepMilliseconds,
            RampDownStepMilliseconds = pipeline.CapturePacing.RampDownStepMilliseconds,
            MaxDelayMilliseconds = pipeline.CapturePacing.MaxDelayMilliseconds
        };

        options.RemoteDispatch = new RemoteDispatchOptions
        {
            Enabled = pipeline.RemoteDispatch.Enabled,
            Mode = ParseEnum(pipeline.RemoteDispatch.Mode, RemoteDispatchMode.None),
            S3Bucket = pipeline.RemoteDispatch.S3Bucket,
            FanoutExchange = pipeline.RemoteDispatch.FanoutExchange,
            Region = pipeline.RemoteDispatch.Region
        };

        var orderedFilters = pipeline.Filters
            .OrderBy(f => f.DisplayOrder)
            .ThenBy(f => f.Name)
            .ToList();

        options.Filters = orderedFilters
            .Select(filter => new FrameFilterOption
            {
                Name = filter.Name,
                Enabled = filter.Enabled,
                Order = filter.DisplayOrder
            })
            .ToArray();

        options.FrameFilters = orderedFilters
            .Select(f => f.Name)
            .ToArray();

        ApplyCameraPipelineOverrides(options);

        static void ApplyExposureProfiles(CameraPipelineOptions options, CameraPipelineConfigEntity pipeline)
        {
            const int minBound = 1;
            const int maxBound = 60_000;

            static int Normalize(int value, int fallback, int min, int max)
            {
                var candidate = value > 0 ? value : fallback;
                return Math.Clamp(candidate, min, max);
            }

            var dayMin = Normalize(pipeline.DayMinimumExposureMilliseconds, options.DayMinExposureMilliseconds, minBound, maxBound);
            var dayMax = Normalize(pipeline.DayMaximumExposureMilliseconds, Math.Max(options.DayMaxExposureMilliseconds, dayMin), dayMin, maxBound);
            var nightMin = Normalize(pipeline.NightMinimumExposureMilliseconds, options.NightMinExposureMilliseconds, minBound, maxBound);
            var nightMax = Normalize(pipeline.NightMaximumExposureMilliseconds, Math.Max(options.NightMaxExposureMilliseconds, nightMin), nightMin, maxBound);

            options.DayMinExposureMilliseconds = dayMin;
            options.DayMaxExposureMilliseconds = dayMax;
            options.NightMinExposureMilliseconds = nightMin;
            options.NightMaxExposureMilliseconds = nightMax;

            var dayStart = pipeline.DayStartExposureMilliseconds > 0 ? pipeline.DayStartExposureMilliseconds : options.DayStartExposureMilliseconds;
            var nightStart = pipeline.NightStartExposureMilliseconds > 0 ? pipeline.NightStartExposureMilliseconds : options.NightStartExposureMilliseconds;

            options.DayStartExposureMilliseconds = Math.Clamp(dayStart, dayMin, dayMax);
            options.NightStartExposureMilliseconds = Math.Clamp(nightStart, nightMin, nightMax);
            options.DayExposureMilliseconds = Math.Clamp(pipeline.DayExposureMilliseconds, dayMin, dayMax);
            options.NightExposureMilliseconds = Math.Clamp(pipeline.NightExposureMilliseconds, nightMin, nightMax);
        }

        static void ApplyGainProfiles(CameraPipelineOptions options, CameraPipelineConfigEntity pipeline)
        {
            const int minGain = 0;
            const int maxGain = 500;

            static int NormalizeGain(int value, int fallback, int min, int max)
            {
                var candidate = value switch
                {
                    < 0 => fallback,
                    _ => value
                };
                return Math.Clamp(candidate, min, max);
            }

            var dayMin = NormalizeGain(pipeline.DayMinimumGain, options.DayMinGain, minGain, maxGain);
            var dayMax = NormalizeGain(pipeline.DayMaximumGain, Math.Max(options.DayMaxGain, dayMin), dayMin, maxGain);
            var nightMin = NormalizeGain(pipeline.NightMinimumGain, options.NightMinGain, minGain, maxGain);
            var nightMax = NormalizeGain(pipeline.NightMaximumGain, Math.Max(options.NightMaxGain, nightMin), nightMin, maxGain);

            options.DayMinGain = dayMin;
            options.DayMaxGain = dayMax;
            options.NightMinGain = nightMin;
            options.NightMaxGain = nightMax;

            var dayStartGain = pipeline.DayStartGain > 0 ? pipeline.DayStartGain : options.DayStartGain;
            var nightStartGain = pipeline.NightStartGain > 0 ? pipeline.NightStartGain : options.NightStartGain;

            options.DayStartGain = Math.Clamp(dayStartGain, dayMin, dayMax);
            options.NightStartGain = Math.Clamp(nightStartGain, nightMin, nightMax);
            options.DayGain = Math.Clamp(pipeline.DayGain, dayMin, dayMax);
            options.NightGain = Math.Clamp(pipeline.NightGain, nightMin, nightMax);
        }
    }

    public void Configure(CardinalDirectionsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var snapshot = GetSnapshot();
        var cardinal = snapshot.Pipeline.CardinalDirections;

        options.OffsetXPixels = cardinal.OffsetXPixels;
        options.OffsetYPixels = cardinal.OffsetYPixels;
        options.RotationDegrees = cardinal.RotationDegrees;
        options.RadiusOffsetPixels = cardinal.RadiusOffsetPixels;
        options.LabelNorth = cardinal.LabelNorth;
        options.LabelSouth = cardinal.LabelSouth;
        options.LabelEast = cardinal.LabelEast;
        options.LabelWest = cardinal.LabelWest;
        options.SwapEastWest = cardinal.SwapEastWest;
        options.CircleColor = cardinal.CircleColor;
        options.CircleOpacity = cardinal.CircleOpacity;
        options.CircleThickness = cardinal.CircleThickness;
        options.CircleLineStyle = ParseEnum(cardinal.CircleLineStyle, CardinalLineStyle.Solid);
        options.LabelFillOpacity = cardinal.LabelFillOpacity;
        options.LabelPadding = cardinal.LabelPadding;
        options.LabelCornerRadius = cardinal.LabelCornerRadius;
        options.LabelFontSize = cardinal.LabelFontSize;
    }

    public void Configure(CircularApertureMaskOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var snapshot = GetSnapshot();
        var mask = snapshot.Pipeline.CircularApertureMask;

        options.OffsetXPixels = mask.OffsetXPixels;
        options.OffsetYPixels = mask.OffsetYPixels;
        options.RadiusOffsetPixels = mask.RadiusOffsetPixels;
        options.MaskColor = mask.MaskColor;
        options.MaskOpacity = mask.MaskOpacity;
    }

    public void Configure(CelestialAnnotationsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var snapshot = GetSnapshot();
        var celestial = snapshot.Pipeline.CelestialAnnotations;

        options.UseAutomaticStarSelection = celestial.UseAutomaticStarSelection;
        options.AutoStarCount = celestial.AutoStarCount;
        options.AutoStarMagnitudeLimit = celestial.AutoStarMagnitudeLimit;
        options.AnnotatePlanets = celestial.AnnotatePlanets;
        options.LabelFontSize = (float)celestial.LabelFontSize;
        options.StarLabelColor = celestial.StarLabelColor;
        options.PlanetLabelColor = celestial.PlanetLabelColor;
        options.DeepSkyLabelColor = celestial.DeepSkyLabelColor;
        options.StarRingRadius = (float)celestial.StarRingRadius;
        options.PlanetRingRadius = (float)celestial.PlanetRingRadius;
        options.DeepSkyRingRadius = (float)celestial.DeepSkyRingRadius;

        options.DeepSkyObjects = celestial.DeepSkyObjects
            .OrderBy(o => o.Name)
            .Select(o => new DeepSkyObjectOption
            {
                Name = o.Name,
                RightAscensionHours = o.RightAscensionHours,
                DeclinationDegrees = o.DeclinationDegrees,
                Magnitude = o.Magnitude,
                Color = o.Color
            })
            .ToList();

        ApplyCelestialOverrides(options);
    }

    public void Configure(ConstellationFigureOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var snapshot = GetSnapshot();
        var figures = snapshot.Pipeline.ConstellationFigures;

        options.LineThickness = (float)figures.LineThickness;
        options.LineOpacity = (float)figures.LineOpacity;
        options.LineColor = figures.LineColor;
        options.UseDashedLine = figures.UseDashedLine;
    }

    public void Configure(StarCatalogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var snapshot = GetSnapshot();
        var star = snapshot.StarCatalog;

        options.MagnitudeLimit = star.MagnitudeLimit;
        options.MinMaxAltitudeDegrees = star.MinMaxAltitudeDegrees;
        options.TopStarCount = star.TopStarCount;
        options.StratifiedSelection = star.StratifiedSelection;
        options.IncludePlanets = star.IncludePlanets;
        options.IncludeMoon = star.IncludeMoon;
        options.IncludeOuterPlanets = star.IncludeOuterPlanets;
        options.IncludeSun = star.IncludeSun;
        options.RightAscensionBins = star.RightAscensionBins;
        options.DeclinationBands = star.DeclinationBands;
    }

    private void ApplyCameraPipelineOverrides(CameraPipelineOptions options)
    {
        var section = _configuration.GetSection("CameraPipeline:Overrides");
        if (!section.Exists())
        {
            return;
        }

        if (TryGetBool(section, nameof(CameraPipelineOptions.EnableStacking), out var enableStacking))
        {
            options.EnableStacking = enableStacking;
            _logger?.LogDebug("Camera pipeline override applied for {Option}: {Value}", nameof(CameraPipelineOptions.EnableStacking), enableStacking);
        }

        var hasBackgroundOverride = false;
        if (TryGetBool(section, "BackgroundStackerEnabled", out var backgroundEnabled))
        {
            options.BackgroundStacker.Enabled = backgroundEnabled;
            hasBackgroundOverride = true;
            _logger?.LogDebug("Camera pipeline override applied for {Option}: {Value}", "BackgroundStackerEnabled", backgroundEnabled);
        }

        if (TryGetBool(section, nameof(CameraPipelineOptions.EnableImageOverlays), out var enableOverlays))
        {
            options.EnableImageOverlays = enableOverlays;
            _logger?.LogDebug("Camera pipeline override applied for {Option}: {Value}", nameof(CameraPipelineOptions.EnableImageOverlays), enableOverlays);
        }

        if (TryGetInt(section, nameof(CameraPipelineOptions.StackingFrameCount), out var stackingFrameCount) && stackingFrameCount > 0)
        {
            options.StackingFrameCount = stackingFrameCount;
            _logger?.LogDebug("Camera pipeline override applied for {Option}: {Value}", nameof(CameraPipelineOptions.StackingFrameCount), stackingFrameCount);
        }

        if (TryGetInt(section, nameof(CameraPipelineOptions.StackingBufferMinimumFrames), out var stackingBufferMinimumFrames) && stackingBufferMinimumFrames > 0)
        {
            options.StackingBufferMinimumFrames = stackingBufferMinimumFrames;
            _logger?.LogDebug("Camera pipeline override applied for {Option}: {Value}", nameof(CameraPipelineOptions.StackingBufferMinimumFrames), stackingBufferMinimumFrames);
        }

        if (TryGetInt(section, nameof(CameraPipelineOptions.StackingBufferIntegrationSeconds), out var stackingBufferIntegrationSeconds) && stackingBufferIntegrationSeconds >= 0)
        {
            options.StackingBufferIntegrationSeconds = stackingBufferIntegrationSeconds;
            _logger?.LogDebug("Camera pipeline override applied for {Option}: {Value}", nameof(CameraPipelineOptions.StackingBufferIntegrationSeconds), stackingBufferIntegrationSeconds);
        }

        var frameFiltersSection = section.GetSection(nameof(CameraPipelineOptions.FrameFilters));
        if (frameFiltersSection.Exists())
        {
            var filters = frameFiltersSection
                .GetChildren()
                .Select(child => child.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim())
                .ToArray();

            if (filters.Length > 0)
            {
                options.FrameFilters = filters;
                options.Filters = filters
                    .Select((name, index) => new FrameFilterOption
                    {
                        Name = name,
                        Enabled = true,
                        Order = index
                    })
                    .ToArray();

                _logger?.LogDebug("Camera pipeline frame filter override applied. Filters: {Filters}", filters);
            }
        }

        if (TryGetBool(section, "DisableSensorNoise", out var disableSensorNoise))
        {
            Environment.SetEnvironmentVariable("HVO_DISABLE_SENSOR_NOISE", disableSensorNoise ? "1" : "0");
            _logger?.LogDebug("Camera pipeline override applied for {Option}: {Value}", "DisableSensorNoise", disableSensorNoise);
        }

        if (TryGetFloat(section, "SensorNoiseScale", out var sensorNoiseScale) && sensorNoiseScale >= 0f)
        {
            var clamped = Math.Clamp(sensorNoiseScale, 0f, 2f);
            Environment.SetEnvironmentVariable("HVO_SENSOR_NOISE_SCALE", clamped.ToString(CultureInfo.InvariantCulture));
            _logger?.LogDebug("Camera pipeline override applied for {Option}: {Value}", "SensorNoiseScale", clamped);
        }

        if (!options.EnableStacking && !hasBackgroundOverride)
        {
            options.BackgroundStacker.Enabled = false;
            options.StackingFrameCount = Math.Max(1, options.StackingFrameCount);
            options.StackingBufferMinimumFrames = Math.Max(1, options.StackingBufferMinimumFrames);
            options.StackingBufferIntegrationSeconds = 0;
        }
    }

    /// <summary>
    /// Returns adapter descriptors for camera registration.
    /// </summary>
    public IReadOnlyList<CameraAdapterDescriptor> GetCameraAdapters()
    {
        var snapshot = GetSnapshot();
        if (snapshot.Adapters.Count == 0)
        {
            return Array.Empty<CameraAdapterDescriptor>();
        }

        var rigLookup = snapshot.Rigs.ToDictionary(rig => rig.Id, rig => rig.Key);

        return snapshot.Adapters
            .Select(adapter => new CameraAdapterDescriptor(
                adapter.Name,
                adapter.AdapterType,
                rigLookup.TryGetValue(adapter.RigId, out var rigKey) ? rigKey : null))
            .ToArray();
    }

    private ConfigurationSnapshot GetSnapshot()
    {
        lock (_snapshotLock)
        {
            if (_snapshot is not null)
            {
                return _snapshot;
            }

            using var context = _contextFactory.CreateDbContext();
            context.Database.Migrate();

            var observatory = context.ObservatorySites.AsNoTracking().OrderBy(site => site.Id).FirstOrDefault()
                ?? throw new InvalidOperationException("Observatory configuration is missing from the SkyMonitor data store.");

            var cameras = context.CameraCatalog.AsNoTracking().OrderBy(camera => camera.Id).ToList();
            var optics = context.OpticsCatalog.AsNoTracking().OrderBy(optic => optic.Id).ToList();
            var rigs = context.RigCatalogEntries.AsNoTracking().OrderBy(rig => rig.Id).ToList();
            var adapters = context.CameraAdapters.AsNoTracking().OrderBy(adapter => adapter.Id).ToList();

            var pipeline = context.CameraPipelineConfigurations.AsNoTracking()
                .AsSplitQuery()
                .Include(p => p.Filters)
                .Include(p => p.CelestialAnnotations.DeepSkyObjects)
                .SingleOrDefault()
                ?? throw new InvalidOperationException("Camera pipeline configuration is missing from the SkyMonitor data store.");

            var starCatalog = context.StarCatalogSettings.AsNoTracking().OrderBy(entry => entry.Id).FirstOrDefault()
                ?? throw new InvalidOperationException("Star catalog configuration is missing from the SkyMonitor data store.");

            var systemSettings = context.SystemSettings.AsNoTracking().OrderBy(setting => setting.Id).ToList();
            var settingsLookup = systemSettings.Count == 0
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : systemSettings.ToDictionary(
                    setting => setting.Key,
                    setting => setting.PayloadJson ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase);

            _snapshot = new ConfigurationSnapshot(observatory, cameras, optics, rigs, adapters, pipeline, starCatalog, settingsLookup);
            return _snapshot;
        }
    }

    public void InvalidateSnapshot()
    {
        lock (_snapshotLock)
        {
            _snapshot = null;
        }
    }

    private void ApplyCelestialOverrides(CelestialAnnotationsOptions options)
    {
        var section = _configuration.GetSection(CelestialAnnotationsOptions.SectionName);
        if (!section.Exists())
        {
            return;
        }

        if (TryGetFloat(section, nameof(CelestialAnnotationsOptions.LabelFontSize), out var labelFont) && labelFont > 0f)
        {
            options.LabelFontSize = Math.Clamp(labelFont, 4f, 72f);
            _logger?.LogDebug("Celestial annotations override applied for LabelFontSize: {LabelFontSize}", options.LabelFontSize);
        }

        if (TryGetColor(section, nameof(CelestialAnnotationsOptions.StarLabelColor), out var starLabelColor))
        {
            options.StarLabelColor = starLabelColor;
            _logger?.LogDebug("Celestial annotations override applied for StarLabelColor: {StarLabelColor}", options.StarLabelColor);
        }

        if (TryGetColor(section, nameof(CelestialAnnotationsOptions.PlanetLabelColor), out var planetLabelColor))
        {
            options.PlanetLabelColor = planetLabelColor;
            _logger?.LogDebug("Celestial annotations override applied for PlanetLabelColor: {PlanetLabelColor}", options.PlanetLabelColor);
        }

        if (TryGetColor(section, nameof(CelestialAnnotationsOptions.DeepSkyLabelColor), out var deepSkyLabelColor))
        {
            options.DeepSkyLabelColor = deepSkyLabelColor;
            _logger?.LogDebug("Celestial annotations override applied for DeepSkyLabelColor: {DeepSkyLabelColor}", options.DeepSkyLabelColor);
        }

        if (TryGetFloat(section, nameof(CelestialAnnotationsOptions.StarRingRadius), out var starRing) && starRing > 0f)
        {
            options.StarRingRadius = Math.Clamp(starRing, 1f, 64f);
            _logger?.LogDebug("Celestial annotations override applied for StarRingRadius: {StarRingRadius}", options.StarRingRadius);
        }

        if (TryGetFloat(section, nameof(CelestialAnnotationsOptions.PlanetRingRadius), out var planetRing) && planetRing > 0f)
        {
            options.PlanetRingRadius = Math.Clamp(planetRing, 1f, 64f);
            _logger?.LogDebug("Celestial annotations override applied for PlanetRingRadius: {PlanetRingRadius}", options.PlanetRingRadius);
        }

        if (TryGetFloat(section, nameof(CelestialAnnotationsOptions.DeepSkyRingRadius), out var deepSkyRing) && deepSkyRing > 0f)
        {
            options.DeepSkyRingRadius = Math.Clamp(deepSkyRing, 1f, 64f);
            _logger?.LogDebug("Celestial annotations override applied for DeepSkyRingRadius: {DeepSkyRingRadius}", options.DeepSkyRingRadius);
        }
    }

    private static bool TryGetBool(IConfiguration section, string key, out bool value)
    {
        var raw = section[key];
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = default;
            return false;
        }

        if (bool.TryParse(raw, out value))
        {
            return true;
        }

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
        {
            value = numeric != 0;
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetInt(IConfiguration section, string key, out int value)
    {
        var raw = section[key];
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = default;
            return false;
        }

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetFloat(IConfiguration section, string key, out float value)
    {
        var raw = section[key];
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = default;
            return false;
        }

        return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetColor(IConfiguration section, string key, out string color)
    {
        var raw = section[key];
        if (string.IsNullOrWhiteSpace(raw))
        {
            color = string.Empty;
            return false;
        }

        var normalized = NormalizeHex(raw);
        if (!IsValidHexColor(normalized))
        {
            color = string.Empty;
            return false;
        }

        color = normalized;
        return true;
    }

    private static string NormalizeHex(string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith('#'))
        {
            trimmed = "#" + trimmed;
        }

        return trimmed.ToUpperInvariant();
    }

    private static bool IsValidHexColor(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (value.Length != 7 && value.Length != 9)
        {
            return false;
        }

        for (var i = 1; i < value.Length; i++)
        {
            if (!Uri.IsHexDigit(value[i]))
            {
                return false;
            }
        }

        return true;
    }

    private CameraCatalogEntryOptions CreateCameraOption(CameraCatalogEntity entity)
    {
        var capabilities = DeserializeStringArray(entity.AdditionalTagsJson);

        return new CameraCatalogEntryOptions
        {
            Name = entity.Key,
            Sensor = new SensorSpecOptions
            {
                WidthPx = entity.SensorWidthPixels,
                HeightPx = entity.SensorHeightPixels,
                PixelSizeMicrons = entity.PixelSizeMicrons,
                CxPx = entity.SensorCxPixels,
                CyPx = entity.SensorCyPixels
            },
            Capabilities = new CameraCapabilitiesOptions
            {
                ColorMode = ParseEnum(entity.ColorMode, CameraColorMode.Unknown),
                SensorTechnology = ParseEnum(entity.SensorTechnology, CameraSensorTechnology.Unknown),
                BodyType = ParseEnum(entity.BodyType, CameraBodyType.Unknown),
                Cooling = ParseEnum(entity.Cooling, CameraCoolingType.None),
                SupportsGainControl = entity.SupportsGainControl,
                SupportsExposureControl = entity.SupportsExposureControl,
                SupportsTemperatureTelemetry = entity.SupportsTemperatureTelemetry,
                SupportsSoftwareBinning = entity.SupportsSoftwareBinning,
                AdditionalTags = capabilities
            },
            Descriptor = new CameraDescriptorOptions
            {
                Manufacturer = entity.Manufacturer,
                Model = entity.Model,
                DriverVersion = entity.DriverVersion,
                AdapterName = entity.AdapterName,
                Capabilities = capabilities
            },
            DriverId = ParseEnum(entity.DriverId, CameraDriverId.Unknown),
            IsSynthetic = entity.IsSynthetic,
            SyntheticProfile = entity.SyntheticProfile
        };
    }

    private static LensCatalogEntryOptions CreateLensOption(OpticsCatalogEntity entity)
        => new()
        {
            Name = entity.Key,
            Lens = new LensSpecOptions
            {
                Name = entity.DisplayName,
                Model = ParseEnum(entity.ProjectionModel, ProjectionModel.Equidistant),
                FocalLengthMm = entity.FocalLengthMillimeters,
                FovXDeg = entity.FieldOfViewXDegrees,
                FovYDeg = entity.FieldOfViewYDegrees ?? entity.FieldOfViewXDegrees,
                RollDeg = entity.RollDegrees,
                Kind = ParseEnum(entity.Kind, LensKind.Rectilinear)
            }
        };

    private static string[] DeserializeStringArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static TEnum ParseEnum<TEnum>(string? value, TEnum defaultValue)
        where TEnum : struct, Enum
        => Enum.TryParse(value, ignoreCase: true, out TEnum parsed) ? parsed : defaultValue;

    private static TelemetryRetentionPolicy ClonePolicy(TelemetryRetentionPolicy? source, TelemetryRetentionPolicy? fallback)
    {
        var template = source ?? fallback ?? TelemetryRetentionPolicy.Create(TimeSpan.FromDays(30), 5_000);
        return TelemetryRetentionPolicy.Create(template.MaxAge, template.MaxRecords);
    }

    private sealed class ConfigurationSnapshot
    {
        public ConfigurationSnapshot(
            ObservatorySiteEntity observatory,
            IReadOnlyList<CameraCatalogEntity> cameras,
            IReadOnlyList<OpticsCatalogEntity> optics,
            IReadOnlyList<RigCatalogEntryEntity> rigs,
            IReadOnlyList<CameraAdapterConfigEntity> adapters,
            CameraPipelineConfigEntity pipeline,
            StarCatalogSettingsEntity starCatalog,
            IReadOnlyDictionary<string, string> systemSettings)
        {
            Observatory = observatory ?? throw new ArgumentNullException(nameof(observatory));
            Cameras = cameras ?? throw new ArgumentNullException(nameof(cameras));
            Optics = optics ?? throw new ArgumentNullException(nameof(optics));
            Rigs = rigs ?? throw new ArgumentNullException(nameof(rigs));
            Adapters = adapters ?? throw new ArgumentNullException(nameof(adapters));
            Pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            StarCatalog = starCatalog ?? throw new ArgumentNullException(nameof(starCatalog));
            SystemSettings = systemSettings ?? throw new ArgumentNullException(nameof(systemSettings));
        }

        public ObservatorySiteEntity Observatory { get; }

    public IReadOnlyList<CameraCatalogEntity> Cameras { get; }

    public IReadOnlyList<OpticsCatalogEntity> Optics { get; }

        public IReadOnlyList<RigCatalogEntryEntity> Rigs { get; }

        public IReadOnlyList<CameraAdapterConfigEntity> Adapters { get; }

        public CameraPipelineConfigEntity Pipeline { get; }

        public StarCatalogSettingsEntity StarCatalog { get; }

        public IReadOnlyDictionary<string, string> SystemSettings { get; }
    }

    public sealed record CameraAdapterDescriptor(string Name, string AdapterType, string? RigKey);
}
