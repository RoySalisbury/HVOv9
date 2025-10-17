#nullable enable
using System;

namespace HVO.SkyMonitorV5.RPi.Cameras.Projection
{
    /// <summary>Projection math helpers (standalone; optional in current pipeline).</summary>
    public static class ProjectionMath
    {
        public readonly struct ProjectionBasis
        {
            public ProjectionBasis(ProjectionVector b, ProjectionVector e1, ProjectionVector e2)
            {
                B = b;
                E1 = e1;
                E2 = e2;
            }

            public ProjectionVector B { get; }
            public ProjectionVector E1 { get; }
            public ProjectionVector E2 { get; }
        }

        public readonly struct ProjectionVector
        {
            public ProjectionVector(double x, double y, double z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public double X { get; }
            public double Y { get; }
            public double Z { get; }

            public static ProjectionVector CreateUnit(double x, double y, double z)
            {
                var length = Math.Sqrt((x * x) + (y * y) + (z * z));
                if (length < 1e-12)
                {
                    return new ProjectionVector(1.0, 0.0, 0.0);
                }

                var scale = 1.0 / length;
                return new ProjectionVector(x * scale, y * scale, z * scale);
            }

            public double LengthSquared => (X * X) + (Y * Y) + (Z * Z);

            public ProjectionVector Normalize()
            {
                var length = Math.Sqrt(LengthSquared);
                if (length < 1e-12)
                {
                    return new ProjectionVector(1.0, 0.0, 0.0);
                }

                var scale = 1.0 / length;
                return new ProjectionVector(X * scale, Y * scale, Z * scale);
            }
        }

        private const double Deg2Rad = Math.PI / 180.0;

        public static ProjectionVector DirFromAltAz(double altDeg, double azDeg)
        {
            var alt = altDeg * Deg2Rad;
            var az = azDeg * Deg2Rad;
            var ca = Math.Cos(alt);
            return new ProjectionVector(ca * Math.Sin(az), ca * Math.Cos(az), Math.Sin(alt));
        }

        public static ProjectionBasis BuildBasis(double alt0Deg, double az0Deg)
        {
            var b = DirFromAltAz(alt0Deg, az0Deg);
            var up = new ProjectionVector(0.0, 0.0, 1.0);
            var e1 = Cross(up, b).Normalize();
            var e2 = Cross(b, e1);
            return new ProjectionBasis(b, e1, e2);
        }

        public static double Dot(in ProjectionVector a, in ProjectionVector c)
            => (a.X * c.X) + (a.Y * c.Y) + (a.Z * c.Z);

        public static ProjectionVector Cross(in ProjectionVector a, in ProjectionVector c)
            => new(
                (a.Y * c.Z) - (a.Z * c.Y),
                (a.Z * c.X) - (a.X * c.Z),
                (a.X * c.Y) - (a.Y * c.X));

        public static double Norm(in ProjectionVector a)
            => Math.Sqrt(Dot(a, a));

        // TODO(dotnet10): replace custom struct with generic-math vector once .NET 10 exposes double-friendly SIMD helpers.
    }
}
