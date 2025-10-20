using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.RPi.Cameras.Drivers;
using HVO.SkyMonitorV5.Data.Configurations;
using HVO.SkyMonitorV5.Data.Configurations.Entities;
using HVO.SkyMonitorV5.RPi.Infrastructure;

using HVO.SkyMonitorV5.RPi.Models.Catalog;
using HVO.SkyMonitorV5.RPi.Models.Cameras;
using HVO.SkyMonitorV5.RPi.Models.Optics;
using HVO.SkyMonitorV5.RPi.Models.Rigs;
using HVO.SkyMonitorV5.RPi.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
namespace HVO.SkyMonitorV5.RPi.Services;

public sealed class EquipmentConfigurationService : IEquipmentConfigurationService
{
    private readonly IDbContextFactory<SkyMonitorConfigurationContext> _contextFactory;
    private readonly IConfigurationSnapshotInvalidator _snapshotInvalidator;
    private readonly IRigRuntimeUpdater _runtimeUpdater;
    private readonly ICameraDriverRegistry _driverRegistry;
    private readonly ILogger<EquipmentConfigurationService>? _logger;
    private static readonly JsonSerializerOptions CatalogSerializerOptions = new(JsonSerializerDefaults.Web);

    public EquipmentConfigurationService(
        IDbContextFactory<SkyMonitorConfigurationContext> contextFactory,
        IConfigurationSnapshotInvalidator snapshotInvalidator,
        IRigRuntimeUpdater runtimeUpdater,
        ICameraDriverRegistry driverRegistry,
        ILogger<EquipmentConfigurationService>? logger = null)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _snapshotInvalidator = snapshotInvalidator ?? throw new ArgumentNullException(nameof(snapshotInvalidator));
        _runtimeUpdater = runtimeUpdater ?? throw new ArgumentNullException(nameof(runtimeUpdater));
        _driverRegistry = driverRegistry ?? throw new ArgumentNullException(nameof(driverRegistry));
        _logger = logger;
    }

    public async Task<Result<EquipmentCatalogResponse>> GetCatalogAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var catalog = await BuildCatalogAsync(context, cancellationToken).ConfigureAwait(false);
            return Result<EquipmentCatalogResponse>.Success(catalog);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to retrieve equipment catalog.");
            return Result<EquipmentCatalogResponse>.Failure(ex);
        }
    }

    public Task<Result<CameraDriverCatalogResponse>> GetCameraDriversAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var descriptors = _driverRegistry.GetDrivers()
                .OrderBy(descriptor => descriptor.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(descriptor => new CameraDriverDescriptorResponse
                {
                    Id = descriptor.Id,
                    DisplayName = descriptor.DisplayName,
                    Description = descriptor.Description,
                    Version = descriptor.Version,
                    ConfigurationType = descriptor.ConfigurationType?.FullName,
                    AssemblyQualifiedName = descriptor.ConfigurationType?.AssemblyQualifiedName,
                    SupportsConfiguration = descriptor.ConfigurationType is not null
                })
                .ToArray();

            var response = new CameraDriverCatalogResponse
            {
                Drivers = descriptors
            };

            return Task.FromResult(Result<CameraDriverCatalogResponse>.Success(response));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to retrieve camera driver descriptors.");
            return Task.FromResult(Result<CameraDriverCatalogResponse>.Failure(ex));
        }
    }
    public async Task<Result<EquipmentCatalogResponse>> CreateRigAsync(CreateRigRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        try
        {
            var normalizedKey = NormalizeKey(request.Key);
            var normalizedName = NormalizeName(request.DisplayName);
            var cameraKey = NormalizeKey(request.CameraKey);
            var opticsKey = NormalizeKey(request.OpticsKey);

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            if (await context.RigCatalogEntries.AnyAsync(rig => rig.Key == normalizedKey, cancellationToken).ConfigureAwait(false))
            {
                return Result<EquipmentCatalogResponse>.Failure(new InvalidOperationException($"A rig with key '{normalizedKey}' already exists."));
            }

            var camera = await context.CameraCatalog
                .FirstOrDefaultAsync(camera => camera.Key == cameraKey, cancellationToken)
                .ConfigureAwait(false);
            if (camera is null)
            {
                return Result<EquipmentCatalogResponse>.Failure(new InvalidOperationException($"Camera '{cameraKey}' was not found in the catalog."));
            }

            var optics = await context.OpticsCatalog
                .FirstOrDefaultAsync(optics => optics.Key == opticsKey, cancellationToken)
                .ConfigureAwait(false);
            if (optics is null)
            {
                return Result<EquipmentCatalogResponse>.Failure(new InvalidOperationException($"Optics '{opticsKey}' was not found in the catalog."));
            }

            var rig = new RigCatalogEntryEntity
            {
                Key = normalizedKey,
                DisplayName = normalizedName,
                CameraId = camera.Id,
                LensId = optics.Id,
                BoresightAltitudeDegrees = ClampAltitude(request.BoresightAltitudeDegrees),
                BoresightAzimuthDegrees = ClampAzimuth(request.BoresightAzimuthDegrees),
                IsActive = request.IsActive,
                Revision = 1
            };

            await context.RigCatalogEntries.AddAsync(rig, cancellationToken).ConfigureAwait(false);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (request.IsActive)
            {
                await DeactivateOtherRigsAsync(context, rig.Id, cancellationToken).ConfigureAwait(false);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            _snapshotInvalidator.InvalidateSnapshot();

            var catalog = await BuildCatalogAsync(context, cancellationToken).ConfigureAwait(false);
            await ReloadRigAdapterAsync(request.IsActive, CancellationToken.None).ConfigureAwait(false);
            return Result<EquipmentCatalogResponse>.Success(catalog);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to create rig configuration.");
            return Result<EquipmentCatalogResponse>.Failure(ex);
        }
    }

    public async Task<Result<EquipmentCatalogResponse>> CreateCameraAsync(CreateCameraRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        try
        {
            var normalizedKey = NormalizeKey(request.Key);
            var normalizedName = NormalizeName(request.DisplayName);
            var normalizedDriverId = NormalizeOptional(request.DriverId);
            var normalizedDriverSettings = NormalizeDriverSettings(normalizedKey, normalizedDriverId, request.DriverSettingsJson);

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            if (await context.CameraCatalog.AnyAsync(camera => camera.Key == normalizedKey, cancellationToken).ConfigureAwait(false))
            {
                return Result<EquipmentCatalogResponse>.Failure(new InvalidOperationException($"A camera with key '{normalizedKey}' already exists."));
            }

            var timestamp = DateTime.UtcNow;

            var camera = new CameraCatalogEntity
            {
                Key = normalizedKey,
                DisplayName = normalizedName,
                Manufacturer = NormalizeOptional(request.Manufacturer),
                Model = NormalizeOptional(request.Model),
                DriverVersion = NormalizeOptional(request.DriverVersion),
                AdapterName = NormalizeOptional(request.AdapterName),
                DriverId = normalizedDriverId,
                IsSynthetic = request.IsSynthetic,
                SyntheticProfile = NormalizeOptional(request.SyntheticProfile),
                SensorWidthPixels = request.SensorWidthPixels,
                SensorHeightPixels = request.SensorHeightPixels,
                PixelSizeMicrons = request.PixelSizeMicrons,
                SensorCxPixels = request.SensorCxPixels,
                SensorCyPixels = request.SensorCyPixels,
                ColorMode = NormalizeOptional(request.ColorMode),
                SensorTechnology = NormalizeOptional(request.SensorTechnology),
                BodyType = NormalizeOptional(request.BodyType),
                Cooling = NormalizeOptional(request.Cooling),
                SupportsGainControl = request.SupportsGainControl,
                SupportsExposureControl = request.SupportsExposureControl,
                SupportsTemperatureTelemetry = request.SupportsTemperatureTelemetry,
                SupportsSoftwareBinning = request.SupportsSoftwareBinning,
                AdditionalTagsJson = SerializeTags(request.AdditionalTags),
                DriverSettingsJson = normalizedDriverSettings,
                CreatedUtc = timestamp,
                UpdatedUtc = timestamp,
                IsActive = request.IsActive,
                Revision = 1
            };

            await context.CameraCatalog.AddAsync(camera, cancellationToken).ConfigureAwait(false);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _snapshotInvalidator.InvalidateSnapshot();

            var catalog = await BuildCatalogAsync(context, cancellationToken).ConfigureAwait(false);
            return Result<EquipmentCatalogResponse>.Success(catalog);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to create camera catalog entry.");
            return Result<EquipmentCatalogResponse>.Failure(ex);
        }
    }

    public async Task<Result<EquipmentCatalogResponse>> UpdateCameraAsync(int cameraId, UpdateCameraRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        try
        {
            var normalizedName = NormalizeName(request.DisplayName);
            var normalizedDriverId = NormalizeOptional(request.DriverId);

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var camera = await context.CameraCatalog
                .FirstOrDefaultAsync(entry => entry.Id == cameraId, cancellationToken)
                .ConfigureAwait(false);
            if (camera is null)
            {
                return Result<EquipmentCatalogResponse>.Failure(new InvalidOperationException($"Camera with id {cameraId} was not found."));
            }

            if (request.Revision <= 0 || request.Revision != camera.Revision)
            {
                return Result<EquipmentCatalogResponse>.Failure(new InvalidOperationException($"Camera '{camera.Key}' has been updated by another request (expected revision {camera.Revision}, received {request.Revision})."));
            }

            var normalizedDriverSettings = NormalizeDriverSettings(camera.Key, normalizedDriverId, request.DriverSettingsJson);

            camera.DisplayName = normalizedName;
            camera.Manufacturer = NormalizeOptional(request.Manufacturer);
            camera.Model = NormalizeOptional(request.Model);
            camera.DriverVersion = NormalizeOptional(request.DriverVersion);
            camera.AdapterName = NormalizeOptional(request.AdapterName);
            camera.DriverId = normalizedDriverId;
            camera.IsSynthetic = request.IsSynthetic;
            camera.SyntheticProfile = NormalizeOptional(request.SyntheticProfile);
            camera.SensorWidthPixels = request.SensorWidthPixels;
            camera.SensorHeightPixels = request.SensorHeightPixels;
            camera.PixelSizeMicrons = request.PixelSizeMicrons;
            camera.SensorCxPixels = request.SensorCxPixels;
            camera.SensorCyPixels = request.SensorCyPixels;
            camera.ColorMode = NormalizeOptional(request.ColorMode);
            camera.SensorTechnology = NormalizeOptional(request.SensorTechnology);
            camera.BodyType = NormalizeOptional(request.BodyType);
            camera.Cooling = NormalizeOptional(request.Cooling);
            camera.SupportsGainControl = request.SupportsGainControl;
            camera.SupportsExposureControl = request.SupportsExposureControl;
            camera.SupportsTemperatureTelemetry = request.SupportsTemperatureTelemetry;
            camera.SupportsSoftwareBinning = request.SupportsSoftwareBinning;
            camera.AdditionalTagsJson = SerializeTags(request.AdditionalTags);
            camera.DriverSettingsJson = normalizedDriverSettings;
            camera.IsActive = request.IsActive;
            camera.UpdatedUtc = DateTime.UtcNow;
            camera.Revision = NextRevision(camera.Revision);

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _snapshotInvalidator.InvalidateSnapshot();

            var catalog = await BuildCatalogAsync(context, cancellationToken).ConfigureAwait(false);
            return Result<EquipmentCatalogResponse>.Success(catalog);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to update camera catalog entry (ID {CameraId}).", cameraId);
            return Result<EquipmentCatalogResponse>.Failure(ex);
        }
    }

    public async Task<Result<EquipmentCatalogResponse>> CreateOpticsAsync(CreateOpticsRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        try
        {
            var normalizedKey = NormalizeKey(request.Key);
            var normalizedName = NormalizeName(request.DisplayName);

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            if (await context.OpticsCatalog.AnyAsync(optics => optics.Key == normalizedKey, cancellationToken).ConfigureAwait(false))
            {
                return Result<EquipmentCatalogResponse>.Failure(new InvalidOperationException($"Optics with key '{normalizedKey}' already exists."));
            }

            var timestamp = DateTime.UtcNow;

            var optics = new OpticsCatalogEntity
            {
                Key = normalizedKey,
                DisplayName = normalizedName,
                ProjectionModel = NormalizeOptional(request.ProjectionModel),
                FocalLengthMillimeters = request.FocalLengthMillimeters,
                FieldOfViewXDegrees = request.FieldOfViewXDegrees,
                FieldOfViewYDegrees = request.FieldOfViewYDegrees,
                RollDegrees = request.RollDegrees,
                Kind = NormalizeOptional(request.Kind),
                CreatedUtc = timestamp,
                UpdatedUtc = timestamp,
                IsActive = request.IsActive,
                Revision = 1
            };

            await context.OpticsCatalog.AddAsync(optics, cancellationToken).ConfigureAwait(false);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _snapshotInvalidator.InvalidateSnapshot();

            var catalog = await BuildCatalogAsync(context, cancellationToken).ConfigureAwait(false);
            return Result<EquipmentCatalogResponse>.Success(catalog);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to create optics catalog entry.");
            return Result<EquipmentCatalogResponse>.Failure(ex);
        }
    }

    public async Task<Result<EquipmentCatalogResponse>> UpdateOpticsAsync(int opticsId, UpdateOpticsRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        try
        {
            var normalizedName = NormalizeName(request.DisplayName);

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var optics = await context.OpticsCatalog
                .FirstOrDefaultAsync(entry => entry.Id == opticsId, cancellationToken)
                .ConfigureAwait(false);
            if (optics is null)
            {
                return Result<EquipmentCatalogResponse>.Failure(new InvalidOperationException($"Optics with id {opticsId} was not found."));
            }

            if (request.Revision <= 0 || request.Revision != optics.Revision)
            {
                return Result<EquipmentCatalogResponse>.Failure(new InvalidOperationException($"Optics '{optics.Key}' has been updated by another request (expected revision {optics.Revision}, received {request.Revision})."));
            }

            optics.DisplayName = normalizedName;
            optics.ProjectionModel = NormalizeOptional(request.ProjectionModel);
            optics.FocalLengthMillimeters = request.FocalLengthMillimeters;
            optics.FieldOfViewXDegrees = request.FieldOfViewXDegrees;
            optics.FieldOfViewYDegrees = request.FieldOfViewYDegrees;
            optics.RollDegrees = request.RollDegrees;
            optics.Kind = NormalizeOptional(request.Kind);
            optics.IsActive = request.IsActive;
            optics.UpdatedUtc = DateTime.UtcNow;
            optics.Revision = NextRevision(optics.Revision);

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _snapshotInvalidator.InvalidateSnapshot();

            var catalog = await BuildCatalogAsync(context, cancellationToken).ConfigureAwait(false);
            return Result<EquipmentCatalogResponse>.Success(catalog);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to update optics catalog entry (ID {OpticsId}).", opticsId);
            return Result<EquipmentCatalogResponse>.Failure(ex);
        }
    }

    public async Task<Result<EquipmentCatalogResponse>> UpdateRigAsync(int rigId, UpdateRigRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        try
        {
            var normalizedName = NormalizeName(request.DisplayName);
            var cameraKey = NormalizeKey(request.CameraKey);
            var opticsKey = NormalizeKey(request.OpticsKey);

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var rig = await context.RigCatalogEntries
                .FirstOrDefaultAsync(entry => entry.Id == rigId, cancellationToken)
                .ConfigureAwait(false);
            if (rig is null)
            {
                return Result<EquipmentCatalogResponse>.Failure(new InvalidOperationException($"Rig with id {rigId} was not found."));
            }

            if (request.Revision <= 0 || request.Revision != rig.Revision)
            {
                return Result<EquipmentCatalogResponse>.Failure(new InvalidOperationException($"Rig '{rig.Key}' has been updated by another request (expected revision {rig.Revision}, received {request.Revision})."));
            }

            var wasActive = rig.IsActive;

            var camera = await context.CameraCatalog
                .FirstOrDefaultAsync(camera => camera.Key == cameraKey, cancellationToken)
                .ConfigureAwait(false);
            if (camera is null)
            {
                return Result<EquipmentCatalogResponse>.Failure(new InvalidOperationException($"Camera '{cameraKey}' was not found in the catalog."));
            }

            var optics = await context.OpticsCatalog
                .FirstOrDefaultAsync(optics => optics.Key == opticsKey, cancellationToken)
                .ConfigureAwait(false);
            if (optics is null)
            {
                return Result<EquipmentCatalogResponse>.Failure(new InvalidOperationException($"Optics '{opticsKey}' was not found in the catalog."));
            }

            rig.DisplayName = normalizedName;
            rig.CameraId = camera.Id;
            rig.LensId = optics.Id;
            rig.BoresightAltitudeDegrees = ClampAltitude(request.BoresightAltitudeDegrees);
            rig.BoresightAzimuthDegrees = ClampAzimuth(request.BoresightAzimuthDegrees);
            rig.IsActive = request.IsActive;

            if (wasActive && !request.IsActive)
            {
                var othersActive = await context.RigCatalogEntries
                    .AnyAsync(other => other.Id != rig.Id && other.IsActive, cancellationToken)
                    .ConfigureAwait(false);

                if (!othersActive)
                {
                    rig.IsActive = wasActive;
                    return Result<EquipmentCatalogResponse>.Failure(new InvalidOperationException("At least one rig must remain active. Activate another rig before disabling this one."));
                }
            }

            rig.Revision = NextRevision(rig.Revision);

            if (request.IsActive)
            {
                await DeactivateOtherRigsAsync(context, rig.Id, cancellationToken).ConfigureAwait(false);
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _snapshotInvalidator.InvalidateSnapshot();

            var catalog = await BuildCatalogAsync(context, cancellationToken).ConfigureAwait(false);
            await ReloadRigAdapterAsync(wasActive || request.IsActive, CancellationToken.None).ConfigureAwait(false);
            return Result<EquipmentCatalogResponse>.Success(catalog);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to update rig configuration (ID {RigId}).", rigId);
            return Result<EquipmentCatalogResponse>.Failure(ex);
        }
    }

    public async Task<Result<EquipmentCatalogResponse>> DeleteRigAsync(int rigId, long? revision, CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var rig = await context.RigCatalogEntries
                .FirstOrDefaultAsync(entry => entry.Id == rigId, cancellationToken)
                .ConfigureAwait(false);
            if (rig is null)
            {
                return Result<EquipmentCatalogResponse>.Failure(new InvalidOperationException($"Rig with id {rigId} was not found."));
            }

            if (revision is > 0 && revision != rig.Revision)
            {
                return Result<EquipmentCatalogResponse>.Failure(new InvalidOperationException($"Rig '{rig.Key}' has been updated by another request (expected revision {rig.Revision}, received {revision})."));
            }



            var wasActive = rig.IsActive;

            if (wasActive)
            {
                var othersActive = await context.RigCatalogEntries
                    .AnyAsync(other => other.Id != rig.Id && other.IsActive, cancellationToken)
                    .ConfigureAwait(false);

                if (!othersActive)
                {
                    return Result<EquipmentCatalogResponse>.Failure(new InvalidOperationException("Cannot delete the last active rig. Activate another rig before deleting this entry."));
                }
            }

            context.RigCatalogEntries.Remove(rig);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _snapshotInvalidator.InvalidateSnapshot();

            var catalog = await BuildCatalogAsync(context, cancellationToken).ConfigureAwait(false);
            await ReloadRigAdapterAsync(wasActive, CancellationToken.None).ConfigureAwait(false);
            return Result<EquipmentCatalogResponse>.Success(catalog);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to delete optics rig configuration (ID {RigId}).", rigId);
            return Result<EquipmentCatalogResponse>.Failure(ex);
        }
    }



    private static string NormalizeKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Catalog keys must not be empty.");
        }

        return value.Trim();
    }

    private static string NormalizeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Display name must not be empty.");
        }

        return value.Trim();
    }

    private static string NormalizeOptional(string value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private string? NormalizeDriverSettings(string cameraKey, string? driverId, string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        var trimmedDriverId = string.IsNullOrWhiteSpace(driverId) ? null : driverId.Trim();

        Result<CameraDriverSettingsPayload> result;
        if (!string.IsNullOrWhiteSpace(trimmedDriverId) && _driverRegistry.TryGetDriver(trimmedDriverId, out var descriptor))
        {
            result = CameraDriverSettingsHelper.Resolve(json, descriptor);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(trimmedDriverId))
            {
                _logger?.LogWarning(
                    "Camera '{CameraKey}' specifies driver id {DriverId}, but the driver registry has no matching descriptor. Settings will be stored without typed validation.",
                    cameraKey,
                    trimmedDriverId);
            }

            result = CameraDriverSettingsHelper.Resolve(json);
        }

        if (!result.IsSuccessful)
        {
            throw new InvalidOperationException(
                $"Driver settings JSON for camera '{cameraKey}' could not be parsed.",
                result.Error ?? new InvalidOperationException("Driver settings validation failed."));
        }

        var payload = result.Value;
        return payload.HasRawJson
            ? JsonSerializer.Serialize(payload.RawJson, CatalogSerializerOptions)
            : null;
    }

    private static string SerializeTags(IReadOnlyList<string>? tags)
    {
        if (tags is null || tags.Count == 0)
        {
            return "[]";
        }

        var normalized = tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalized.Length == 0
            ? "[]"
            : JsonSerializer.Serialize(normalized, CatalogSerializerOptions);
    }

    private static double ClampAltitude(double altitude)
        => Math.Clamp(altitude, 0.0, 90.0);

    private static double ClampAzimuth(double azimuth)
        => Math.Clamp(azimuth, 0.0, 360.0);

    private static long NextRevision(long current)
        => current <= 0 ? 1 : current + 1;

    private static async Task DeactivateOtherRigsAsync(SkyMonitorConfigurationContext context, int activeRigId, CancellationToken cancellationToken)
    {
        var others = await context.RigCatalogEntries
            .Where(rig => rig.Id != activeRigId && rig.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var rig in others)
        {
            rig.IsActive = false;
            rig.Revision = NextRevision(rig.Revision);
        }
    }

    private async Task ReloadRigAdapterAsync(bool forceRestart, CancellationToken cancellationToken)
    {
        try
        {
            await _runtimeUpdater.ReloadActiveRigAsync(forceRestart, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger?.LogDebug("Rig runtime reload cancelled by caller.");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to reload rig acquisition adapter after catalog update.");
        }
    }

    private static async Task<EquipmentCatalogResponse> BuildCatalogAsync(SkyMonitorConfigurationContext context, CancellationToken cancellationToken)
    {
        var rigs = await context.RigCatalogEntries
            .AsNoTracking()
            .Include(rig => rig.Camera)
            .Include(rig => rig.Lens)
            .OrderBy(rig => rig.DisplayName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);



    var camerasInUse = rigs.Select(rig => rig.CameraId).ToHashSet();
    var opticsInUse = rigs.Select(rig => rig.LensId).ToHashSet();

        var cameraEntities = await context.CameraCatalog
            .AsNoTracking()
            .OrderBy(camera => camera.DisplayName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var cameras = cameraEntities
            .Select(camera => new CameraCatalogItem
            {
                Id = camera.Id,
                Revision = camera.Revision,
                Key = camera.Key,
                DisplayName = camera.DisplayName,
                Manufacturer = camera.Manufacturer,
                Model = camera.Model,
                DriverVersion = camera.DriverVersion,
                AdapterName = camera.AdapterName,
                DriverId = camera.DriverId,
                IsSynthetic = camera.IsSynthetic,
                SyntheticProfile = camera.SyntheticProfile,
                SensorWidthPixels = camera.SensorWidthPixels,
                SensorHeightPixels = camera.SensorHeightPixels,
                PixelSizeMicrons = camera.PixelSizeMicrons,
                SensorCxPixels = camera.SensorCxPixels,
                SensorCyPixels = camera.SensorCyPixels,
                ColorMode = camera.ColorMode,
                SensorTechnology = camera.SensorTechnology,
                BodyType = camera.BodyType,
                Cooling = camera.Cooling,
                SupportsGainControl = camera.SupportsGainControl,
                SupportsExposureControl = camera.SupportsExposureControl,
                SupportsTemperatureTelemetry = camera.SupportsTemperatureTelemetry,
                SupportsSoftwareBinning = camera.SupportsSoftwareBinning,
                AdditionalTags = DeserializeTags(camera.AdditionalTagsJson),
                DriverSettingsJson = camera.DriverSettingsJson,
                CreatedUtc = camera.CreatedUtc,
                UpdatedUtc = camera.UpdatedUtc,
                IsActive = camera.IsActive,
                IsInUse = camerasInUse.Contains(camera.Id)
            })
            .ToList();

        var opticsEntities = await context.OpticsCatalog
            .AsNoTracking()
            .OrderBy(lens => lens.DisplayName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var optics = opticsEntities
            .Select(lens => new OpticsCatalogItem
            {
                Id = lens.Id,
                Revision = lens.Revision,
                Key = lens.Key,
                DisplayName = lens.DisplayName,
                ProjectionModel = lens.ProjectionModel,
                FocalLengthMillimeters = lens.FocalLengthMillimeters,
                FieldOfViewXDegrees = lens.FieldOfViewXDegrees,
                FieldOfViewYDegrees = lens.FieldOfViewYDegrees,
                RollDegrees = lens.RollDegrees,
                Kind = lens.Kind,
                CreatedUtc = lens.CreatedUtc,
                UpdatedUtc = lens.UpdatedUtc,
                IsActive = lens.IsActive,
                IsInUse = opticsInUse.Contains(lens.Id)
            })
            .ToList();

        var activeRigKey = rigs.FirstOrDefault(rig => rig.IsActive)?.Key;

        var summaries = rigs
            .Select(rig => new RigSummary
            {
                Id = rig.Id,
                Key = rig.Key,
                DisplayName = rig.DisplayName,
                CameraKey = rig.Camera?.Key ?? string.Empty,
                CameraDisplayName = rig.Camera?.DisplayName ?? string.Empty,
                OpticsKey = rig.Lens?.Key ?? string.Empty,
                OpticsDisplayName = rig.Lens?.DisplayName ?? string.Empty,
                BoresightAltitudeDegrees = rig.BoresightAltitudeDegrees,
                BoresightAzimuthDegrees = rig.BoresightAzimuthDegrees,
                IsActive = rig.IsActive,

                Revision = rig.Revision
            })
            .ToList();



        return new EquipmentCatalogResponse
        {
            Rigs = summaries,
            Cameras = cameras,
            Optics = optics,
            ActiveRigKey = activeRigKey
        };
    }



    private static IReadOnlyList<string> DeserializeTags(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }

        try
        {
            var tags = JsonSerializer.Deserialize<string[]?>(json, CatalogSerializerOptions);
            return tags?.Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(tag => tag.Trim()).ToArray() ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}
