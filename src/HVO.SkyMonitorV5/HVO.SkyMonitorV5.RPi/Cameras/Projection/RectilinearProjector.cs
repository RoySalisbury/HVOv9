#nullable enable
using System;
using HVO.SkyMonitorV5.RPi.Cameras.Optics;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;

namespace HVO.SkyMonitorV5.RPi.Cameras.Projection
{
    /// <summary>
    /// Thin adapter to make <see cref="RectilinearLens"/> satisfy <see cref="IImageProjector"/>.
    /// </summary>
    public sealed class RectilinearProjector : IImageProjector
    {
        private readonly RectilinearLens _lens;

        public RectilinearProjector(RectilinearLens lens)
        {
            _lens = lens ?? throw new ArgumentNullException(nameof(lens));
        }

        public int WidthPx  => _lens.WidthPx;
        public int HeightPx => _lens.HeightPx;
        public double Cx => _lens.Cx;
        public double Cy => _lens.Cy;
        public ProjectionModel Model => _lens.Model;

        public bool TryProjectRay(double X, double Y, double Z, out float u, out float v)
            => _lens.TryProjectCameraRay(X, Y, Z, out u, out v);

        public (double X, double Y, double Z) PixelToRay(float u, float v)
            => _lens.PixelToCameraRay(u, v);

        public RectilinearLens Lens => _lens;
    }
}
