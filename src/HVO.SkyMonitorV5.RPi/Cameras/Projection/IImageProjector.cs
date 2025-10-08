#nullable enable
using System;
using HVO.SkyMonitorV5.RPi.Cameras.Optics;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;

namespace HVO.SkyMonitorV5.RPi.Cameras.Projection
{
    /// <summary>
    /// Common projector interface: maps a camera-space unit ray (X,Y,Z) to pixel (u,v) and back.
    /// Camera space uses +X right, +Y up, +Z forward. Pixel (0,0) is top-left.
    /// </summary>
    public interface IImageProjector
    {
        int WidthPx { get; }
        int HeightPx { get; }
        double Cx { get; }
        double Cy { get; }
        ProjectionModel Model { get; }

        /// <summary>Project a unit direction vector in camera space to pixel coordinates.</summary>
        bool TryProjectRay(double X, double Y, double Z, out float u, out float v);

        /// <summary>Inverse: map pixel to a unit ray in camera space.</summary>
        (double X, double Y, double Z) PixelToRay(float u, float v);
    }
}
