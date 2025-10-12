using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    IConfigureOptions<StarCatalogOptions>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);

    private readonly IDbContextFactory<SkyMonitorConfigurationContext> _contextFactory;
    private readonly ILogger<DatabaseBackedConfigurationOptionsConfigurator>? _logger;

    private readonly object _snapshotLock = new();
    private ConfigurationSnapshot? _snapshot;

    public DatabaseBackedConfigurationOptionsConfigurator(
        IDbContextFactory<SkyMonitorConfigurationContext> contextFactory,
        ILogger<DatabaseBackedConfigurationOptionsConfigurator>? logger = null)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
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

    public void Configure(AllSkyCatalogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var snapshot = GetSnapshot();

        var cameraLookup = snapshot.Cameras.ToDictionary(c => c.Id);
        var lensLookup = snapshot.Lenses.ToDictionary(l => l.Id);

        options.Cameras = snapshot.Cameras
            .Select(CreateCameraOption)
            .ToList();

        options.Lenses = snapshot.Lenses
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
        options.DayExposureMilliseconds = pipeline.DayExposureMilliseconds;
        options.NightExposureMilliseconds = pipeline.NightExposureMilliseconds;
        options.DayGain = pipeline.DayGain;
        options.NightGain = pipeline.NightGain;
        options.DayNightTransitionHourOffset = pipeline.DayNightTransitionHourOffset;
        options.OverlayTextFormat = pipeline.OverlayTextFormat;

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

            var cameras = context.CameraCatalogCameras.AsNoTracking().OrderBy(camera => camera.Id).ToList();
            var lenses = context.CameraCatalogLenses.AsNoTracking().OrderBy(lens => lens.Id).ToList();
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

            _snapshot = new ConfigurationSnapshot(observatory, cameras, lenses, rigs, adapters, pipeline, starCatalog);
            return _snapshot;
        }
    }

    private CameraCatalogEntryOptions CreateCameraOption(CameraCatalogCameraEntity entity)
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

    private static LensCatalogEntryOptions CreateLensOption(CameraCatalogLensEntity entity)
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

    private sealed record ConfigurationSnapshot(
        ObservatorySiteEntity Observatory,
        IReadOnlyList<CameraCatalogCameraEntity> Cameras,
        IReadOnlyList<CameraCatalogLensEntity> Lenses,
        IReadOnlyList<RigCatalogEntryEntity> Rigs,
        IReadOnlyList<CameraAdapterConfigEntity> Adapters,
        CameraPipelineConfigEntity Pipeline,
        StarCatalogSettingsEntity StarCatalog);

    public sealed record CameraAdapterDescriptor(string Name, string AdapterType, string? RigKey);
}
