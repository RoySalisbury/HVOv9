using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.Data.Configurations;
using HVO.SkyMonitorV5.Data.Configurations.Entities;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Models.Optics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Services;

public sealed class OpticsConfigurationService : IOpticsConfigurationService
{
    private readonly IDbContextFactory<SkyMonitorConfigurationContext> _contextFactory;
    private readonly IConfigurationSnapshotInvalidator _snapshotInvalidator;
    private readonly ILogger<OpticsConfigurationService>? _logger;

    public OpticsConfigurationService(
        IDbContextFactory<SkyMonitorConfigurationContext> contextFactory,
        IConfigurationSnapshotInvalidator snapshotInvalidator,
        ILogger<OpticsConfigurationService>? logger = null)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _snapshotInvalidator = snapshotInvalidator ?? throw new ArgumentNullException(nameof(snapshotInvalidator));
        _logger = logger;
    }

    public async Task<Result<OpticsCatalogResponse>> GetCatalogAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var catalog = await BuildCatalogAsync(context, cancellationToken).ConfigureAwait(false);
            return Result<OpticsCatalogResponse>.Success(catalog);
        }
        catch (Exception ex)
        {
            return Result<OpticsCatalogResponse>.Failure(ex);
        }
    }

    public async Task<Result<OpticsCatalogResponse>> CreateRigAsync(CreateOpticsRigRequest request, CancellationToken cancellationToken)
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
            var lensKey = NormalizeKey(request.LensKey);

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            if (await context.RigCatalogEntries.AnyAsync(rig => rig.Key == normalizedKey, cancellationToken).ConfigureAwait(false))
            {
                return Result<OpticsCatalogResponse>.Failure(new InvalidOperationException($"An optics rig with key '{normalizedKey}' already exists."));
            }

            var camera = await context.CameraCatalogCameras
                .FirstOrDefaultAsync(camera => camera.Key == cameraKey, cancellationToken)
                .ConfigureAwait(false);
            if (camera is null)
            {
                return Result<OpticsCatalogResponse>.Failure(new InvalidOperationException($"Camera '{cameraKey}' was not found in the catalog."));
            }

            var lens = await context.CameraCatalogLenses
                .FirstOrDefaultAsync(lens => lens.Key == lensKey, cancellationToken)
                .ConfigureAwait(false);
            if (lens is null)
            {
                return Result<OpticsCatalogResponse>.Failure(new InvalidOperationException($"Lens '{lensKey}' was not found in the catalog."));
            }

            var rig = new RigCatalogEntryEntity
            {
                Key = normalizedKey,
                DisplayName = normalizedName,
                CameraId = camera.Id,
                LensId = lens.Id,
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

            if (await EnsureAnyActiveRigAsync(context, cancellationToken).ConfigureAwait(false))
            {
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            _snapshotInvalidator.InvalidateSnapshot();

            var catalog = await BuildCatalogAsync(context, cancellationToken).ConfigureAwait(false);
            return Result<OpticsCatalogResponse>.Success(catalog);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to create optics rig configuration.");
            return Result<OpticsCatalogResponse>.Failure(ex);
        }
    }

    public async Task<Result<OpticsCatalogResponse>> UpdateRigAsync(int rigId, UpdateOpticsRigRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        try
        {
            var normalizedName = NormalizeName(request.DisplayName);
            var cameraKey = NormalizeKey(request.CameraKey);
            var lensKey = NormalizeKey(request.LensKey);

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var rig = await context.RigCatalogEntries
                .FirstOrDefaultAsync(entry => entry.Id == rigId, cancellationToken)
                .ConfigureAwait(false);
            if (rig is null)
            {
                return Result<OpticsCatalogResponse>.Failure(new InvalidOperationException($"Optics rig with id {rigId} was not found."));
            }

            if (request.Revision <= 0 || request.Revision != rig.Revision)
            {
                return Result<OpticsCatalogResponse>.Failure(new InvalidOperationException($"Optics rig '{rig.Key}' has been updated by another request (expected revision {rig.Revision}, received {request.Revision})."));
            }

            var camera = await context.CameraCatalogCameras
                .FirstOrDefaultAsync(camera => camera.Key == cameraKey, cancellationToken)
                .ConfigureAwait(false);
            if (camera is null)
            {
                return Result<OpticsCatalogResponse>.Failure(new InvalidOperationException($"Camera '{cameraKey}' was not found in the catalog."));
            }

            var lens = await context.CameraCatalogLenses
                .FirstOrDefaultAsync(lens => lens.Key == lensKey, cancellationToken)
                .ConfigureAwait(false);
            if (lens is null)
            {
                return Result<OpticsCatalogResponse>.Failure(new InvalidOperationException($"Lens '{lensKey}' was not found in the catalog."));
            }

            rig.DisplayName = normalizedName;
            rig.CameraId = camera.Id;
            rig.LensId = lens.Id;
            rig.BoresightAltitudeDegrees = ClampAltitude(request.BoresightAltitudeDegrees);
            rig.BoresightAzimuthDegrees = ClampAzimuth(request.BoresightAzimuthDegrees);
            rig.IsActive = request.IsActive;
            rig.Revision = NextRevision(rig.Revision);

            if (request.IsActive)
            {
                await DeactivateOtherRigsAsync(context, rig.Id, cancellationToken).ConfigureAwait(false);
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (!request.IsActive && await EnsureAnyActiveRigAsync(context, cancellationToken).ConfigureAwait(false))
            {
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            _snapshotInvalidator.InvalidateSnapshot();

            var catalog = await BuildCatalogAsync(context, cancellationToken).ConfigureAwait(false);
            return Result<OpticsCatalogResponse>.Success(catalog);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to update optics rig configuration (ID {RigId}).", rigId);
            return Result<OpticsCatalogResponse>.Failure(ex);
        }
    }

    public async Task<Result<OpticsCatalogResponse>> DeleteRigAsync(int rigId, long? revision, CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var rig = await context.RigCatalogEntries
                .FirstOrDefaultAsync(entry => entry.Id == rigId, cancellationToken)
                .ConfigureAwait(false);
            if (rig is null)
            {
                return Result<OpticsCatalogResponse>.Failure(new InvalidOperationException($"Optics rig with id {rigId} was not found."));
            }

            if (revision is > 0 && revision != rig.Revision)
            {
                return Result<OpticsCatalogResponse>.Failure(new InvalidOperationException($"Optics rig '{rig.Key}' has been updated by another request (expected revision {rig.Revision}, received {revision})."));
            }

            var isReferenced = await context.CameraAdapters
                .AnyAsync(adapter => adapter.RigId == rig.Id, cancellationToken)
                .ConfigureAwait(false);
            if (isReferenced)
            {
                return Result<OpticsCatalogResponse>.Failure(new InvalidOperationException($"Optics rig '{rig.Key}' is in use by a camera adapter and cannot be deleted."));
            }

            var wasActive = rig.IsActive;

            context.RigCatalogEntries.Remove(rig);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (wasActive && await EnsureAnyActiveRigAsync(context, cancellationToken).ConfigureAwait(false))
            {
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            _snapshotInvalidator.InvalidateSnapshot();

            var catalog = await BuildCatalogAsync(context, cancellationToken).ConfigureAwait(false);
            return Result<OpticsCatalogResponse>.Success(catalog);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to delete optics rig configuration (ID {RigId}).", rigId);
            return Result<OpticsCatalogResponse>.Failure(ex);
        }
    }

    private static string NormalizeKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Optics rig keys must not be empty.");
        }

        return value.Trim();
    }

    private static string NormalizeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Optics rig display name must not be empty.");
        }

        return value.Trim();
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

    private static async Task<bool> EnsureAnyActiveRigAsync(SkyMonitorConfigurationContext context, CancellationToken cancellationToken)
    {
        if (await context.RigCatalogEntries.AnyAsync(rig => rig.IsActive, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var fallback = await context.RigCatalogEntries
            .OrderBy(rig => rig.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (fallback is null)
        {
            return false;
        }

        fallback.IsActive = true;
        fallback.Revision = NextRevision(fallback.Revision);
        return true;
    }

    private static async Task<OpticsCatalogResponse> BuildCatalogAsync(SkyMonitorConfigurationContext context, CancellationToken cancellationToken)
    {
        var rigs = await context.RigCatalogEntries
            .AsNoTracking()
            .Include(rig => rig.Camera)
            .Include(rig => rig.Lens)
            .OrderBy(rig => rig.DisplayName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var adapterLookup = await context.CameraAdapters
            .AsNoTracking()
            .GroupBy(adapter => adapter.RigId)
            .Select(group => new { RigId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.RigId, entry => entry.Count, cancellationToken)
            .ConfigureAwait(false);

        var cameras = await context.CameraCatalogCameras
            .AsNoTracking()
            .OrderBy(camera => camera.DisplayName)
            .Select(camera => new OpticsCatalogCamera
            {
                Key = camera.Key,
                DisplayName = camera.DisplayName,
                Manufacturer = camera.Manufacturer,
                Model = camera.Model,
                SensorWidthPixels = camera.SensorWidthPixels,
                SensorHeightPixels = camera.SensorHeightPixels
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var lenses = await context.CameraCatalogLenses
            .AsNoTracking()
            .OrderBy(lens => lens.DisplayName)
            .Select(lens => new OpticsCatalogLens
            {
                Key = lens.Key,
                DisplayName = lens.DisplayName,
                ProjectionModel = lens.ProjectionModel,
                FocalLengthMillimeters = lens.FocalLengthMillimeters,
                FieldOfViewXDegrees = lens.FieldOfViewXDegrees,
                FieldOfViewYDegrees = lens.FieldOfViewYDegrees
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var activeRigKey = rigs.FirstOrDefault(rig => rig.IsActive)?.Key;

        var summaries = rigs
            .Select(rig => new OpticsRigSummary
            {
                Id = rig.Id,
                Key = rig.Key,
                DisplayName = rig.DisplayName,
                CameraKey = rig.Camera?.Key ?? string.Empty,
                CameraDisplayName = rig.Camera?.DisplayName ?? string.Empty,
                LensKey = rig.Lens?.Key ?? string.Empty,
                LensDisplayName = rig.Lens?.DisplayName ?? string.Empty,
                BoresightAltitudeDegrees = rig.BoresightAltitudeDegrees,
                BoresightAzimuthDegrees = rig.BoresightAzimuthDegrees,
                IsActive = rig.IsActive,
                HasAdapterBindings = adapterLookup.ContainsKey(rig.Id),
                Revision = rig.Revision
            })
            .ToList();

        return new OpticsCatalogResponse
        {
            Rigs = summaries,
            Cameras = cameras,
            Lenses = lenses,
            ActiveRigKey = activeRigKey
        };
    }
}
