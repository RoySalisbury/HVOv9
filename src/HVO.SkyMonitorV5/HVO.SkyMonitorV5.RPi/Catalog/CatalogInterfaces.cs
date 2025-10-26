#nullable enable
using System.Collections.Generic;
using HVO;
using HVO.SkyMonitorV5.RPi.Cameras.Optics;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;

namespace HVO.SkyMonitorV5.RPi.Catalog;

/// <summary>
/// Resolves camera specifications by name from the configured catalog.
/// </summary>
public interface ICameraCatalog
{
    Result<CameraSpec> Resolve(string name);

    IReadOnlyList<CameraSpec> List();
}

/// <summary>
/// Resolves lens specifications by name from the configured catalog.
/// </summary>
public interface ILensCatalog
{
    Result<LensSpec> Resolve(string name);

    IReadOnlyList<LensSpec> List();
}

/// <summary>
/// Resolves complete rig specifications including camera and lens composition.
/// </summary>
public interface IRigCatalog
{
    Result<RigSpec> Resolve(string name);

    IReadOnlyList<RigSpec> List();

    Result<RigSpec> ResolveActive();
}
