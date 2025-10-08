using HVO.SkyMonitorV5.RPi.Cameras.Rendering;

namespace HVO.SkyMonitorV5.RPi.Cameras.Optics;

public sealed record LensSpec(
    ProjectionModel Model,   // Equidistant, Equisolid, Perspective, ...
    double FocalLengthMm,    // 2.7 for FE185C086HA-1 (fisheye); telescope FL otherwise
    double FovXDeg,          // for convenience & override; derive if unknown
    double? FovYDeg = null,  // optional
    double RollDeg = 0.0,    // camera roll about optical axis
    string Name = "",
    LensKind Kind = LensKind.Rectilinear);
