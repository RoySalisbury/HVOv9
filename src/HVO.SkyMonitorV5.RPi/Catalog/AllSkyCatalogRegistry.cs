#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using HVO;
using HVO.SkyMonitorV5.RPi.Cameras.Optics;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.SkyMonitorV5.RPi.Catalog;

/// <summary>
/// Default in-memory catalog implementation backed by <see cref="AllSkyCatalogOptions"/>.
/// </summary>
public sealed class AllSkyCatalogRegistry : ICameraCatalog, ILensCatalog, IRigCatalog
{
    private readonly IOptionsMonitor<AllSkyCatalogOptions> _options;
    private readonly ILogger<AllSkyCatalogRegistry>? _logger;

    public AllSkyCatalogRegistry(
        IOptionsMonitor<AllSkyCatalogOptions> options,
        ILogger<AllSkyCatalogRegistry>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
    }

    Result<CameraSpec> ICameraCatalog.Resolve(string name)
        => ResolveCamera(name);

    IReadOnlyList<CameraSpec> ICameraCatalog.List()
        => CreateSnapshot().CameraList;

    Result<LensSpec> ILensCatalog.Resolve(string name)
        => ResolveLens(name);

    IReadOnlyList<LensSpec> ILensCatalog.List()
        => CreateSnapshot().LensList;

    Result<RigSpec> IRigCatalog.Resolve(string name)
        => ResolveRig(name);

    IReadOnlyList<RigSpec> IRigCatalog.List()
        => CreateSnapshot().RigList;

    Result<RigSpec> IRigCatalog.ResolveActive()
        => ResolveActiveRig();

    private Result<CameraSpec> ResolveCamera(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<CameraSpec>.Failure(new ArgumentException("Camera name is required.", nameof(name)));
        }

        var snapshot = CreateSnapshot();
        if (snapshot.Cameras.TryGetValue(name.Trim(), out var spec))
        {
            return Result<CameraSpec>.Success(spec);
        }

        return Result<CameraSpec>.Failure(new InvalidOperationException($"Camera '{name}' was not found in the catalog."));
    }

    private Result<LensSpec> ResolveLens(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<LensSpec>.Failure(new ArgumentException("Lens name is required.", nameof(name)));
        }

        var snapshot = CreateSnapshot();
        if (snapshot.Lenses.TryGetValue(name.Trim(), out var spec))
        {
            return Result<LensSpec>.Success(spec);
        }

        return Result<LensSpec>.Failure(new InvalidOperationException($"Lens '{name}' was not found in the catalog."));
    }

    private Result<RigSpec> ResolveRig(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<RigSpec>.Failure(new ArgumentException("Rig name is required.", nameof(name)));
        }

        var snapshot = CreateSnapshot();
        if (snapshot.Rigs.TryGetValue(name.Trim(), out var spec))
        {
            return Result<RigSpec>.Success(spec);
        }

        return Result<RigSpec>.Failure(new InvalidOperationException($"Rig '{name}' was not found in the catalog."));
    }

    private Result<RigSpec> ResolveActiveRig()
    {
        var snapshot = CreateSnapshot();
        if (snapshot.ActiveRig is not null)
        {
            return Result<RigSpec>.Success(snapshot.ActiveRig);
        }

        return Result<RigSpec>.Failure(new InvalidOperationException("Active rig is not configured."));
    }

    private CatalogSnapshot CreateSnapshot()
    {
        var options = _options.CurrentValue ?? new AllSkyCatalogOptions();

        var cameraMap = new Dictionary<string, CameraSpec>(StringComparer.OrdinalIgnoreCase);
        var cameraList = new List<CameraSpec>();
        if (options.Cameras is { Count: > 0 })
        {
            foreach (var entry in options.Cameras)
            {
                if (!TryBuildCamera(entry, out var spec))
                {
                    continue;
                }

                cameraMap[spec.Name] = spec;
                cameraList.Add(spec);
            }
        }

        var lensMap = new Dictionary<string, LensSpec>(StringComparer.OrdinalIgnoreCase);
        var lensList = new List<LensSpec>();
        if (options.Lenses is { Count: > 0 })
        {
            foreach (var entry in options.Lenses)
            {
                if (!TryBuildLens(entry, out var spec))
                {
                    continue;
                }

                lensMap[entry.Name.Trim()] = spec;
                lensList.Add(spec);
            }
        }

        var rigMap = new Dictionary<string, RigSpec>(StringComparer.OrdinalIgnoreCase);
        var rigList = new List<RigSpec>();
        RigSpec? activeRig = null;
        if (options.Rigs?.Entries is { Count: > 0 })
        {
            foreach (var entry in options.Rigs.Entries)
            {
                if (!TryBuildRig(entry, cameraMap, lensMap, out var spec))
                {
                    continue;
                }

                rigMap[entry.Name.Trim()] = spec;
                rigList.Add(spec);

                if (activeRig is null && !string.IsNullOrWhiteSpace(options.Rigs.ActiveRig)
                    && string.Equals(entry.Name, options.Rigs.ActiveRig, StringComparison.OrdinalIgnoreCase))
                {
                    activeRig = spec;
                }
            }

            if (activeRig is null && !string.IsNullOrWhiteSpace(options.Rigs.ActiveRig))
            {
                _logger?.LogWarning("Configured active rig {Rig} was not found in the catalog entries.", options.Rigs.ActiveRig);
            }
        }

        return new CatalogSnapshot(cameraMap, cameraList, lensMap, lensList, rigMap, rigList, activeRig);
    }

    private bool TryBuildCamera(CameraCatalogEntryOptions entry, [NotNullWhen(true)] out CameraSpec? spec)
    {
        spec = null;
        if (entry is null)
        {
            return false;
        }

        try
        {
            spec = entry.ToCameraSpec();
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to build camera catalog entry {Camera}.", entry.Name);
            return false;
        }
    }

    private bool TryBuildLens(LensCatalogEntryOptions entry, [NotNullWhen(true)] out LensSpec? spec)
    {
        spec = null;
        if (entry is null)
        {
            return false;
        }

        try
        {
            spec = entry.ToLensSpec();
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to build lens catalog entry {Lens}.", entry.Name);
            return false;
        }
    }

    private bool TryBuildRig(
        RigCatalogEntryOptions entry,
        IReadOnlyDictionary<string, CameraSpec> cameras,
        IReadOnlyDictionary<string, LensSpec> lenses,
        [NotNullWhen(true)] out RigSpec? spec)
    {
        spec = null;
        if (entry is null)
        {
            return false;
        }

        var cameraName = entry.Camera?.Trim();
        var lensName = entry.Lens?.Trim();

        if (string.IsNullOrWhiteSpace(cameraName) || !cameras.TryGetValue(cameraName, out var camera))
        {
            _logger?.LogError("Rig entry {Rig} references unknown camera {Camera}.", entry.Name, entry.Camera);
            return false;
        }

        if (string.IsNullOrWhiteSpace(lensName) || !lenses.TryGetValue(lensName, out var lens))
        {
            _logger?.LogError("Rig entry {Rig} references unknown lens {Lens}.", entry.Name, entry.Lens);
            return false;
        }

        try
        {
            spec = entry.ToRigSpec(camera, lens);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to build rig catalog entry {Rig}.", entry.Name);
            return false;
        }
    }

    private sealed record CatalogSnapshot(
        IReadOnlyDictionary<string, CameraSpec> Cameras,
        IReadOnlyList<CameraSpec> CameraList,
        IReadOnlyDictionary<string, LensSpec> Lenses,
        IReadOnlyList<LensSpec> LensList,
        IReadOnlyDictionary<string, RigSpec> Rigs,
        IReadOnlyList<RigSpec> RigList,
        RigSpec? ActiveRig);
}
