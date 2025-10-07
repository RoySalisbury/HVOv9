#nullable enable
using System;

namespace HVO.SkyMonitorV5.RPi.Cameras.Projection
{
    /// <summary>Projection math helpers (standalone; optional in current pipeline).</summary>
    public static class ProjectionMath
    {
        public readonly struct ProjectionBasis
        {
            public readonly double[] B;   // boresight
            public readonly double[] E1;  // image east at boresight
            public readonly double[] E2;  // image north at boresight
            public ProjectionBasis(double[] b, double[] e1, double[] e2) { B=b; E1=e1; E2=e2; }
        }

        private const double Deg2Rad = Math.PI / 180.0;

        public static double[] DirFromAltAz(double altDeg, double azDeg)
        {
            var alt = altDeg * Deg2Rad;
            var az  = azDeg  * Deg2Rad;
            var ca = Math.Cos(alt);
            return new[] { ca * Math.Sin(az), ca * Math.Cos(az), Math.Sin(alt) };
        }

        public static ProjectionBasis BuildBasis(double alt0Deg, double az0Deg)
        {
            var b  = DirFromAltAz(alt0Deg, az0Deg);
            var up = new[] { 0.0, 0.0, 1.0 };
            var e1 = Cross(up, b);
            var n = Norm(e1);
            if (n < 1e-12) e1 = new[] { 1.0, 0.0, 0.0 }; else { e1[0]/=n; e1[1]/=n; e1[2]/=n; }
            var e2 = Cross(b, e1);
            return new ProjectionBasis(b, e1, e2);
        }

        public static double Dot(double[] a, double[] c) => a[0]*c[0]+a[1]*c[1]+a[2]*c[2];
        public static double[] Cross(double[] a, double[] c) => new[] { a[1]*c[2]-a[2]*c[1], a[2]*c[0]-a[0]*c[2], a[0]*c[1]-a[1]*c[0] };
        public static double Norm(double[] a) => Math.Sqrt(Dot(a,a));
    }
}
