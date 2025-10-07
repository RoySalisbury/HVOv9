#nullable enable
using SkiaSharp;
using System;
using System.Collections.Generic;

namespace HVO.SkyMonitorV5.RPi.Cameras.MockCamera
{
    public enum PlanetMarkerShape { Circle, Square, Diamond }

    public enum PlanetBody
    {
        Mercury, Venus, Mars, Jupiter, Saturn, Uranus, Neptune, Moon, Sun
    }

    public sealed record PlanetMark(string Name, PlanetBody Body, Star Star, SKColor Color);

    public sealed record PlanetRenderOptions(
        float MinRadiusPx = 3.5f,
        float MaxRadiusPx = 9.5f,
        float MoonRadiusPx = 12.0f,
        PlanetMarkerShape Shape = PlanetMarkerShape.Circle)
    {
        public static PlanetRenderOptions Default { get; } = new();
    }

    /// <summary>
    /// Planets: J2000 Kepler elements with secular rates (+1 light-time iteration) → geocentric RA/Dec.
    /// Sun: from Earth's heliocentric vector.
    /// Moon: compact Meeus-style model with dominant terms + topocentric parallax.
    /// </summary>
    public static class PlanetMarks
    {
        private const double ObliquityDeg = 23.439291;
        private const double AU_LIGHT_TIME_DAYS = 0.0057755183; // 1 AU in light-days
        private static readonly DateTime J2000 = new(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        private static readonly IReadOnlyDictionary<PlanetBody, string> Names = new Dictionary<PlanetBody, string>
        {
            [PlanetBody.Mercury] = "Mercury",
            [PlanetBody.Venus]   = "Venus",
            [PlanetBody.Mars]    = "Mars",
            [PlanetBody.Jupiter] = "Jupiter",
            [PlanetBody.Saturn]  = "Saturn",
            [PlanetBody.Uranus]  = "Uranus",
            [PlanetBody.Neptune] = "Neptune",
            [PlanetBody.Moon]    = "Moon",
            [PlanetBody.Sun]     = "Sun"
        };

        private sealed record PlanetStyle(double BaseMagnitude, double Variation, SKColor Color);

        // Display-only styling
        private static readonly IReadOnlyDictionary<PlanetBody, PlanetStyle> Style = new Dictionary<PlanetBody, PlanetStyle>
        {
            [PlanetBody.Mercury] = new(-0.6, 0.8, new SKColor(220,220,220)),
            [PlanetBody.Venus]   = new(-3.9, 0.3, new SKColor(245,245,210)),
            [PlanetBody.Mars]    = new(-1.3, 1.0, new SKColor(255,120, 80)),
            [PlanetBody.Jupiter] = new(-2.2, 0.3, new SKColor(255,215,  0)),
            [PlanetBody.Saturn]  = new(-0.5, 0.3, new SKColor(245,230,170)),
            [PlanetBody.Uranus]  = new( 5.7, 0.2, new SKColor(180,220,255)),
            [PlanetBody.Neptune] = new( 7.8, 0.2, new SKColor(170,190,255)),
            [PlanetBody.Moon]    = new(-12.0,0.6, new SKColor(210,210,210)),
            [PlanetBody.Sun]     = new(-26.7,0.0, new SKColor(255,240,200)),
        };

        private sealed record KeplerElements(
            double a,  double aDot,
            double e,  double eDot,
            double I,  double IDot,
            double L,  double LDot,
            double longPeri,  double longPeriDot,
            double longNode,  double longNodeDot
        );

        // J2000 mean elements + secular rates per Julian century (compact/low-precision set)
        private static readonly Dictionary<PlanetBody, KeplerElements> KeplerJ2000 = new()
        {
            [PlanetBody.Mercury] = new(0.38709927, 0.00000037, 0.20563593,  0.00001906,  7.00497902, -0.00594749,
                                       252.25032350, 149472.67411175, 77.45779628, 0.16047689,  48.33076593, -0.12534081),

            [PlanetBody.Venus]   = new(0.72333566, 0.00000390, 0.00677672, -0.00004107,  3.39467605, -0.00078890,
                                       181.97909950,  58517.81538729, 131.60246718, 0.00268329,  76.67984255, -0.27769418),

            [PlanetBody.Mars]    = new(1.52371034, 0.00001847, 0.09339410,  0.00007882,  1.84969142, -0.00813131,
                                       -4.55343205,  19140.30268499, -23.94362959, 0.44441088,  49.55953891, -0.29257343),

            [PlanetBody.Jupiter] = new(5.20288700,-0.00011607, 0.04838624, -0.00013253,  1.30439695, -0.00183714,
                                        34.39644051,  3034.74612775,  14.72847983, 0.21252668, 100.47390909,  0.20469106),

            [PlanetBody.Saturn]  = new(9.53667594,-0.00125060, 0.05386179, -0.00050991,  2.48599187,  0.00193609,
                                        49.95424423,  1222.49362201,  92.59887831,-0.41897216, 113.66242448, -0.28867794),

            [PlanetBody.Uranus]  = new(19.18916464,-0.00196176,0.04725744, -0.00004397,  0.77263783, -0.00242939,
                                       313.23810451,   428.48202785, 170.95427630, 0.40805281,  74.01692503,  0.04240589),

            [PlanetBody.Neptune] = new(30.06992276, 0.00026291,0.00859048,  0.00005105,  1.77004347,  0.00035372,
                                       -55.12002969,   218.45945325,  44.96476227,-0.32241464, 131.78422574, -0.00508664),

            // Earth's elements (used to get the Sun and geocentric planet vectors)
            [PlanetBody.Sun]     = new(1.00000261, 0.00000562, 0.01671123, -0.00004392, -0.00001531,-0.01294668,
                                       100.46457166, 35999.37244981, 102.93768193, 0.32327364,  0.0,          0.0)
        };

        public static List<PlanetMark> Compute(
            double latitudeDeg,
            double longitudeDeg,
            DateTime utc,
            bool includeUranusNeptune = false,
            bool includeSun = false)
        {
            var bodies = new List<PlanetBody>
            {
                PlanetBody.Mercury, PlanetBody.Venus, PlanetBody.Mars,
                PlanetBody.Jupiter, PlanetBody.Saturn, PlanetBody.Moon
            };
            if (includeUranusNeptune) { bodies.Add(PlanetBody.Uranus); bodies.Add(PlanetBody.Neptune); }
            if (includeSun)           { bodies.Add(PlanetBody.Sun); }

            var marks = new List<PlanetMark>(bodies.Count);

            foreach (var body in bodies)
            {
                Star star;
                var color = Style[body].Color;

                if (body == PlanetBody.Moon)
                {
                    // New: compact lunar model → geocentric RA/Dec + topocentric parallax
                    var (raH, decD, distAu) = MoonGeocentricEquatorial(utc);
                    (raH, decD) = ApplyTopocentricParallax(raH, decD, distAu, latitudeDeg, longitudeDeg, utc);
                    star = new Star(raH, decD, Style[PlanetBody.Moon].BaseMagnitude, color);
                }
                else if (body == PlanetBody.Sun)
                {
                    var (raH, decD, distAu) = GeocentricSunEquatorial(utc);
                    (raH, decD) = ApplyTopocentricParallax(raH, decD, distAu, latitudeDeg, longitudeDeg, utc);
                    star = new Star(raH, decD, Style[PlanetBody.Sun].BaseMagnitude, color);
                }
                else
                {
                    var (raH, decD, distAu) = GeocentricEquatorial(body, utc);
                    (raH, decD) = ApplyTopocentricParallax(raH, decD, distAu, latitudeDeg, longitudeDeg, utc);
                    star = new Star(raH, decD, Style[body].BaseMagnitude, color);
                }

                marks.Add(new PlanetMark(Names[body], body, star, color));
            }

            return marks;
        }

        // ---------------- Planets & Sun ----------------

        private static (double raHours, double decDeg, double distAu) GeocentricEquatorial(PlanetBody body, DateTime utc)
        {
            double jd = ToJulian(utc);
            double T = (jd - 2451545.0) / 36525.0;

            // Earth heliocentric
            var eEl = KeplerJ2000[PlanetBody.Sun];
            var (xE, yE, zE, _) = HeliocentricEcliptic(eEl, T);

            // Planet heliocentric (with one light-time iteration)
            var pEl = KeplerJ2000[body];
            var (xP, yP, zP, _) = HeliocentricEcliptic(pEl, T);

            double X0 = xP - xE, Y0 = yP - yE, Z0 = zP - zE;
            double dist0 = Math.Sqrt(X0 * X0 + Y0 * Y0 + Z0 * Z0);
            double tau = dist0 * AU_LIGHT_TIME_DAYS;

            double Tplanet = ((jd - tau) - 2451545.0) / 36525.0;
            (xP, yP, zP, _) = HeliocentricEcliptic(pEl, Tplanet);

            double X = xP - xE, Y = yP - yE, Z = zP - zE;
            double dist = Math.Sqrt(X * X + Y * Y + Z * Z);

            var (raH, decD) = EclipticVectorToRaDec(X, Y, Z);
            return (raH, decD, dist);
        }

        private static (double raHours, double decDeg, double distAu) GeocentricSunEquatorial(DateTime utc)
        {
            double jd = ToJulian(utc);
            double T = (jd - 2451545.0) / 36525.0;

            var eEl = KeplerJ2000[PlanetBody.Sun];
            var (xE, yE, zE, rE) = HeliocentricEcliptic(eEl, T);

            double X = -xE, Y = -yE, Z = -zE; // geocentric Sun vector
            var (raH, decD) = EclipticVectorToRaDec(X, Y, Z);
            return (raH, decD, rE);
        }

        private static (double x, double y, double z, double r) HeliocentricEcliptic(KeplerElements el, double T)
        {
            double a = el.a + el.aDot * T;
            double e = el.e + el.eDot * T;
            double I = ToRad(el.I + el.IDot * T);
            double L = NormalizeDeg(el.L + el.LDot * T);
            double p = NormalizeDeg(el.longPeri + el.longPeriDot * T); // ϖ
            double O = NormalizeDeg(el.longNode + el.longNodeDot * T); // Ω

            double M = ToRad(NormalizeDeg(L - p));
            double w = ToRad(NormalizeDeg(p - O));

            // Kepler's equation (solve for E)
            double E = M;
            for (int i = 0; i < 6; i++)
            {
                double f = E - e * Math.Sin(E) - M;
                double fp = 1.0 - e * Math.Cos(E);
                E -= f / fp;
            }

            double cosE = Math.Cos(E), sinE = Math.Sin(E);
            double nu = Math.Atan2(Math.Sqrt(1 - e * e) * sinE, cosE - e);
            double r = a * (1 - e * cosE);

            double x_orb = r * Math.Cos(nu);
            double y_orb = r * Math.Sin(nu);

            double cO = Math.Cos(ToRad(O)), sO = Math.Sin(ToRad(O));
            double cI = Math.Cos(I),        sI = Math.Sin(I);
            double cw = Math.Cos(w),        sw = Math.Sin(w);

            double x = (cO * cw - sO * sw * cI) * x_orb + (-cO * sw - sO * cw * cI) * y_orb;
            double y = (sO * cw + cO * sw * cI) * x_orb + (-sO * sw + cO * cw * cI) * y_orb;
            double z = (sw * sI)                    * x_orb + (cw * sI)                    * y_orb;

            return (x, y, z, r); // AU
        }

        private static (double raHours, double decDeg) EclipticVectorToRaDec(double X, double Y, double Z)
        {
            double eps = ToRad(ObliquityDeg);
            double Xe = X;
            double Ye = Y * Math.Cos(eps) - Z * Math.Sin(eps);
            double Ze = Y * Math.Sin(eps) + Z * Math.Cos(eps);

            double ra = Math.Atan2(Ye, Xe);
            if (ra < 0) ra += Math.Tau;
            double dec = Math.Atan2(Ze, Math.Sqrt(Xe * Xe + Ye * Ye));

            return (ra * 12.0 / Math.PI, ToDeg(dec));
        }

        // ---------------- Moon (new) ----------------

        /// <summary>
        /// Geocentric Moon RA/Dec using a compact Meeus-style model (dominant terms only).
        /// Good to ~0.2–0.5° for most dates. Distance returned in AU for parallax.
        /// </summary>
        private static (double raHours, double decDeg, double distAu) MoonGeocentricEquatorial(DateTime utc)
        {
            double jd = ToJulian(utc);
            double T = (jd - 2451545.0) / 36525.0;

            // Mean elements (deg) — Meeus AA (J2000)
            double Lp = NormalizeDeg(218.3164477 + 481267.88123421 * T - 0.0015786 * T * T + T * T * T / 538841.0 - T * T * T * T / 65194000.0); // mean longitude
            double D  = NormalizeDeg(297.8501921 + 445267.1114034 * T - 0.0018819 * T * T + T * T * T / 545868.0 - T * T * T * T / 113065000.0);  // mean elongation
            double M  = NormalizeDeg(357.5291092 + 35999.0502909 * T - 0.0001536 * T * T + T * T * T / 24490000.0);                                // Sun mean anomaly
            double Mp = NormalizeDeg(134.9633964 + 477198.8675055 * T + 0.0087414 * T * T + T * T * T / 69699.0 - T * T * T * T / 14712000.0);     // Moon mean anomaly
            double F  = NormalizeDeg(93.2720950  + 483202.0175233 * T - 0.0036539 * T * T - T * T * T / 3526000.0 + T * T * T * T / 863310000.0);  // argument of latitude

            // Convert to radians
            double Dr = ToRad(D), Mr = ToRad(M), Mpr = ToRad(Mp), Fr = ToRad(F);

            // Low-precision corrections (degrees). Dominant terms only.
            // (Common compact model used by many apps; good for planetarium alignment.)
            double lon = Lp
                + 6.289  * Math.Sin(Mpr)
                + 1.274  * Math.Sin(2 * Dr - Mpr)
                + 0.658  * Math.Sin(2 * Dr)
                + 0.214  * Math.Sin(2 * Mpr)
                - 0.186  * Math.Sin(Mr)
                - 0.114  * Math.Sin(2 * Fr);

            double lat = 5.128 * Math.Sin(Fr)
                + 0.280 * Math.Sin(Mpr + Fr)
                + 0.277 * Math.Sin(Mpr - Fr)
                + 0.173 * Math.Sin(2 * Dr - Fr)
                + 0.055 * Math.Sin(2 * Dr + Fr)
                + 0.046 * Math.Sin(2 * Dr - Mpr + Fr)
                + 0.033 * Math.Sin(2 * Dr - Mpr - Fr)
                + 0.017 * Math.Sin(2 * Mpr + Fr);

            // Distance (km), simplified series → convert to AU
            double distanceKm = 385001.0
                - 20905.0 * Math.Cos(Mpr)
                - 3699.0  * Math.Cos(2 * Dr - Mpr)
                - 2956.0  * Math.Cos(2 * Dr)
                -  570.0  * Math.Cos(2 * Mpr);
            double distAu = distanceKm / 149_597_870.700;

            // Ecliptic → RA/Dec
            var (raH, decD) = EclipticToEquatorial(lon, lat);
            return (raH, decD, distAu);
        }

        private static (double raHours, double decDeg) EclipticToEquatorial(double lonDeg, double latDeg)
        {
            double lon = ToRad(lonDeg);
            double lat = ToRad(latDeg);
            double eps = ToRad(ObliquityDeg);

            double x = Math.Cos(lat) * Math.Cos(lon);
            double y = Math.Cos(lat) * Math.Sin(lon) * Math.Cos(eps) - Math.Sin(lat) * Math.Sin(eps);
            double z = Math.Cos(lat) * Math.Sin(lon) * Math.Sin(eps) + Math.Sin(lat) * Math.Cos(eps);

            double ra = Math.Atan2(y, x);
            if (ra < 0) ra += Math.Tau;
            double dec = Math.Asin(Math.Clamp(z, -1.0, 1.0));
            return (ra * 12.0 / Math.PI, ToDeg(dec));
        }

        // ---------------- Topocentric parallax ----------------

        private static (double RaHours, double DecDeg) ApplyTopocentricParallax(
            double raHours, double decDeg, double distanceAu,
            double latitudeDeg, double longitudeDeg, DateTime utc)
        {
            // Earth equatorial radius ≈ 6378.14 km → AU
            const double EarthRadiusAu = 1.0 / 23455.0;

            double ra = raHours * Math.PI / 12.0;
            double dec = ToRad(decDeg);
            double phi = ToRad(latitudeDeg);

            double lstHours = LocalSiderealTime(utc, longitudeDeg);
            double LST = lstHours * Math.PI / 12.0;

            double H = LST - ra; // hour angle
            double pi = Math.Asin(EarthRadiusAu / Math.Max(distanceAu, 1e-6)); // horizontal parallax (rad)

            // Meeus ch. 40 small-angle topocentric corrections
            double dRA  = -pi * Math.Cos(phi) * Math.Sin(H) / Math.Cos(dec);
            double dDec = -pi * (Math.Sin(phi) * Math.Cos(dec) - Math.Cos(phi) * Math.Cos(H) * Math.Sin(dec));

            double raTop  = ra  + dRA;
            double decTop = dec + dDec;

            double raHoursTop = (raTop * 12.0 / Math.PI) % 24.0;
            if (raHoursTop < 0) raHoursTop += 24.0;

            return (raHoursTop, ToDeg(decTop));
        }

        // ---------------- Utilities ----------------

        private static double LocalSiderealTime(DateTime utc, double longitudeDeg)
        {
            double jd = ToJulian(utc);
            double T = (jd - 2451545.0) / 36525.0;
            double gmst = 6.697374558 + 2400.051336 * T + 0.000025862 * T * T;
            double fractionalDay = (jd + 0.5) % 1.0;
            gmst = (gmst + fractionalDay * 24.0 * 1.00273790935) % 24.0;
            double lst = (gmst + longitudeDeg / 15.0) % 24.0;
            if (lst < 0) lst += 24.0;
            return lst;
        }

        private static double ToJulian(DateTime utc) => utc.ToOADate() + 2415018.5;
        private static double ToRad(double d) => d * Math.PI / 180.0;
        private static double ToDeg(double r) => r * 180.0 / Math.PI;
        private static double NormalizeDeg(double d) { d %= 360.0; return d < 0 ? d + 360.0 : d; }
    }
}
