#nullable enable

using System;
using System.Collections.Generic;
using HVO.Astronomy;

namespace HVO.SkyMonitorV5.RPi.Cameras.MockCamera;

/// <summary>
/// Provides celestial mechanics helpers for computing topocentric planet positions.
/// </summary>
public static class PlanetMath
{
    private const double ObliquityDeg = 23.439291;
    private const double AuLightTimeDays = 0.0057755183; // 1 AU in light-days

    private sealed record KeplerElements(
        double SemiMajorAxis,
        double SemiMajorAxisDot,
        double Eccentricity,
        double EccentricityDot,
        double InclinationDeg,
        double InclinationDot,
        double MeanLongitudeDeg,
        double MeanLongitudeDot,
        double LongitudeOfPerihelionDeg,
        double LongitudeOfPerihelionDot,
        double LongitudeOfAscendingNodeDeg,
        double LongitudeOfAscendingNodeDot);

    private static readonly IReadOnlyDictionary<PlanetBody, KeplerElements> KeplerJ2000 = new Dictionary<PlanetBody, KeplerElements>
    {
        [PlanetBody.Mercury] = new(0.38709927, 0.00000037, 0.20563593,  0.00001906,  7.00497902, -0.00594749,
                                   252.25032350, 149472.67411175,  77.45779628, 0.16047689,  48.33076593, -0.12534081),

        [PlanetBody.Venus] = new(0.72333566, 0.00000390, 0.00677672, -0.00004107,  3.39467605, -0.00078890,
                                 181.97909950,  58517.81538729, 131.60246718, 0.00268329,  76.67984255, -0.27769418),

        [PlanetBody.Mars] = new(1.52371034, 0.00001847, 0.09339410,  0.00007882,  1.84969142, -0.00813131,
                                -4.55343205,  19140.30268499, -23.94362959, 0.44441088,  49.55953891, -0.29257343),

        [PlanetBody.Jupiter] = new(5.20288700,-0.00011607, 0.04838624, -0.00013253,  1.30439695, -0.00183714,
                                    34.39644051,  3034.74612775,  14.72847983, 0.21252668, 100.47390909,  0.20469106),

        [PlanetBody.Saturn] = new(9.53667594,-0.00125060, 0.05386179, -0.00050991,  2.48599187,  0.00193609,
                                   49.95424423,  1222.49362201,  92.59887831,-0.41897216, 113.66242448, -0.28867794),

        [PlanetBody.Uranus] = new(19.18916464,-0.00196176, 0.04725744, -0.00004397,  0.77263783, -0.00242939,
                                   313.23810451,   428.48202785, 170.95427630, 0.40805281,  74.01692503,  0.04240589),

        [PlanetBody.Neptune] = new(30.06992276, 0.00026291, 0.00859048,  0.00005105,  1.77004347,  0.00035372,
                                   -55.12002969,   218.45945325,  44.96476227,-0.32241464, 131.78422574, -0.00508664),

        // Earth's elements (used to derive the Sun and geocentric planet vectors)
        [PlanetBody.Sun] = new(1.00000261, 0.00000562, 0.01671123, -0.00004392, -0.00001531,-0.01294668,
                               100.46457166, 35999.37244981, 102.93768193, 0.32327364,  0.0,          0.0)
    };

    /// <summary>
    /// Describes the topocentric equatorial position of a solar system body.
    /// </summary>
    public sealed record PlanetPosition(PlanetBody Body, double RightAscensionHours, double DeclinationDegrees, double DistanceAu);

    public static IReadOnlyList<PlanetPosition> ComputeTopocentricPositions(
        double latitudeDeg,
        double longitudeDeg,
        DateTime utc,
        IReadOnlyCollection<PlanetBody> bodies)
    {
        if (bodies is null)
        {
            throw new ArgumentNullException(nameof(bodies));
        }

        if (bodies.Count == 0)
        {
            return Array.Empty<PlanetPosition>();
        }

        var results = new List<PlanetPosition>(bodies.Count);
        foreach (var body in bodies)
        {
            results.Add(ComputeTopocentricPosition(body, latitudeDeg, longitudeDeg, utc));
        }

        return results;
    }

    private static PlanetPosition ComputeTopocentricPosition(
        PlanetBody body,
        double latitudeDeg,
        double longitudeDeg,
        DateTime utc)
    {
        (double raHours, double decDeg, double distanceAu) = body switch
        {
            PlanetBody.Moon => MoonGeocentricEquatorial(utc),
            PlanetBody.Sun => GeocentricSunEquatorial(utc),
            _ => GeocentricEquatorial(body, utc)
        };

        (raHours, decDeg) = ApplyTopocentricParallax(raHours, decDeg, distanceAu, latitudeDeg, longitudeDeg, utc);
        return new PlanetPosition(body, raHours, decDeg, distanceAu);
    }

    private static (double raHours, double decDeg, double distAu) GeocentricEquatorial(PlanetBody body, DateTime utc)
    {
        double jd = AstronomyMath.JulianDate(utc);
        double T = (jd - 2451545.0) / 36525.0;

        // Earth heliocentric
        var earth = KeplerJ2000[PlanetBody.Sun];
        var (xE, yE, zE, _) = HeliocentricEcliptic(earth, T);

        // Planet heliocentric with one light-time iteration
        var planetElements = KeplerJ2000[body];
        var (xP, yP, zP, _) = HeliocentricEcliptic(planetElements, T);

        double X0 = xP - xE;
        double Y0 = yP - yE;
        double Z0 = zP - zE;
        double dist0 = Math.Sqrt(X0 * X0 + Y0 * Y0 + Z0 * Z0);
        double tau = dist0 * AuLightTimeDays;

        double Tplanet = ((jd - tau) - 2451545.0) / 36525.0;
        (xP, yP, zP, _) = HeliocentricEcliptic(planetElements, Tplanet);

        double X = xP - xE;
        double Y = yP - yE;
        double Z = zP - zE;
        double dist = Math.Sqrt(X * X + Y * Y + Z * Z);

        var (raH, decD) = EclipticVectorToRaDec(X, Y, Z);
        return (raH, decD, dist);
    }

    private static (double raHours, double decDeg, double distAu) GeocentricSunEquatorial(DateTime utc)
    {
        double jd = AstronomyMath.JulianDate(utc);
        double T = (jd - 2451545.0) / 36525.0;

        var earth = KeplerJ2000[PlanetBody.Sun];
        var (xE, yE, zE, rE) = HeliocentricEcliptic(earth, T);

        double X = -xE;
        double Y = -yE;
        double Z = -zE;

        var (raH, decD) = EclipticVectorToRaDec(X, Y, Z);
        return (raH, decD, rE);
    }

    private static (double x, double y, double z, double radiusAu) HeliocentricEcliptic(KeplerElements elements, double T)
    {
        double a = elements.SemiMajorAxis + elements.SemiMajorAxisDot * T;
        double e = elements.Eccentricity + elements.EccentricityDot * T;
        double I = AstronomyMath.DegreesToRadians(elements.InclinationDeg + elements.InclinationDot * T);
        double L = AstronomyMath.NormalizeDegrees(elements.MeanLongitudeDeg + elements.MeanLongitudeDot * T);
        double p = AstronomyMath.NormalizeDegrees(elements.LongitudeOfPerihelionDeg + elements.LongitudeOfPerihelionDot * T);
        double O = AstronomyMath.NormalizeDegrees(elements.LongitudeOfAscendingNodeDeg + elements.LongitudeOfAscendingNodeDot * T);

        double M = AstronomyMath.DegreesToRadians(AstronomyMath.NormalizeDegrees(L - p));
        double w = AstronomyMath.DegreesToRadians(AstronomyMath.NormalizeDegrees(p - O));

        double E = M;
        for (int i = 0; i < 6; i++)
        {
            double f = E - e * Math.Sin(E) - M;
            double fp = 1.0 - e * Math.Cos(E);
            E -= f / fp;
        }

        double cosE = Math.Cos(E);
        double sinE = Math.Sin(E);
        double nu = Math.Atan2(Math.Sqrt(1 - e * e) * sinE, cosE - e);
        double r = a * (1 - e * cosE);

        double xOrb = r * Math.Cos(nu);
        double yOrb = r * Math.Sin(nu);

        double cO = Math.Cos(AstronomyMath.DegreesToRadians(O));
        double sO = Math.Sin(AstronomyMath.DegreesToRadians(O));
        double cI = Math.Cos(I);
        double sI = Math.Sin(I);
        double cw = Math.Cos(w);
        double sw = Math.Sin(w);

        double x = (cO * cw - sO * sw * cI) * xOrb + (-cO * sw - sO * cw * cI) * yOrb;
        double y = (sO * cw + cO * sw * cI) * xOrb + (-sO * sw + cO * cw * cI) * yOrb;
        double z = (sw * sI) * xOrb + (cw * sI) * yOrb;

        return (x, y, z, r);
    }

    private static (double raHours, double decDeg) EclipticVectorToRaDec(double X, double Y, double Z)
    {
        double eps = AstronomyMath.DegreesToRadians(ObliquityDeg);
        double Xe = X;
        double Ye = Y * Math.Cos(eps) - Z * Math.Sin(eps);
        double Ze = Y * Math.Sin(eps) + Z * Math.Cos(eps);

        double ra = Math.Atan2(Ye, Xe);
        if (ra < 0)
        {
            ra += Math.Tau;
        }

        double dec = Math.Atan2(Ze, Math.Sqrt(Xe * Xe + Ye * Ye));
        return (ra * 12.0 / Math.PI, AstronomyMath.RadiansToDegrees(dec));
    }

    private static (double raHours, double decDeg, double distAu) MoonGeocentricEquatorial(DateTime utc)
    {
        double jd = AstronomyMath.JulianDate(utc);
        double T = (jd - 2451545.0) / 36525.0;

        double Lp = AstronomyMath.NormalizeDegrees(218.3164477 + 481267.88123421 * T - 0.0015786 * T * T + T * T * T / 538841.0 - T * T * T * T / 65194000.0);
        double D  = AstronomyMath.NormalizeDegrees(297.8501921 + 445267.1114034 * T - 0.0018819 * T * T + T * T * T / 545868.0 - T * T * T * T / 113065000.0);
        double M  = AstronomyMath.NormalizeDegrees(357.5291092 + 35999.0502909 * T - 0.0001536 * T * T + T * T * T / 24490000.0);
        double Mp = AstronomyMath.NormalizeDegrees(134.9633964 + 477198.8675055 * T + 0.0087414 * T * T + T * T * T / 69699.0 - T * T * T * T / 14712000.0);
        double F  = AstronomyMath.NormalizeDegrees(93.2720950  + 483202.0175233 * T - 0.0036539 * T * T - T * T * T / 3526000.0 + T * T * T * T / 863310000.0);

        double Dr = AstronomyMath.DegreesToRadians(D);
        double Mr = AstronomyMath.DegreesToRadians(M);
        double Mpr = AstronomyMath.DegreesToRadians(Mp);
        double Fr = AstronomyMath.DegreesToRadians(F);

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

        double distanceKm = 385001.0
            - 20905.0 * Math.Cos(Mpr)
            - 3699.0  * Math.Cos(2 * Dr - Mpr)
            - 2956.0  * Math.Cos(2 * Dr)
            -  570.0  * Math.Cos(2 * Mpr);

        double distAu = distanceKm / 149_597_870.700;

        var (raH, decD) = EclipticToEquatorial(lon, lat);
        return (raH, decD, distAu);
    }

    private static (double raHours, double decDeg) EclipticToEquatorial(double lonDeg, double latDeg)
    {
        double lon = AstronomyMath.DegreesToRadians(lonDeg);
        double lat = AstronomyMath.DegreesToRadians(latDeg);
        double eps = AstronomyMath.DegreesToRadians(ObliquityDeg);

        double x = Math.Cos(lat) * Math.Cos(lon);
        double y = Math.Cos(lat) * Math.Sin(lon) * Math.Cos(eps) - Math.Sin(lat) * Math.Sin(eps);
        double z = Math.Cos(lat) * Math.Sin(lon) * Math.Sin(eps) + Math.Sin(lat) * Math.Cos(eps);

        double ra = Math.Atan2(y, x);
        if (ra < 0)
        {
            ra += Math.Tau;
        }

        double dec = Math.Asin(Math.Clamp(z, -1.0, 1.0));
        return (ra * 12.0 / Math.PI, AstronomyMath.RadiansToDegrees(dec));
    }

    private static (double RaHours, double DecDeg) ApplyTopocentricParallax(
        double raHours,
        double decDeg,
        double distanceAu,
        double latitudeDeg,
        double longitudeDeg,
        DateTime utc)
    {
        const double EarthRadiusAu = 1.0 / 23455.0;

        double ra = raHours * Math.PI / 12.0;
        double dec = AstronomyMath.DegreesToRadians(decDeg);
        double phi = AstronomyMath.DegreesToRadians(latitudeDeg);

        double lstHours = AstronomyMath.LocalSiderealTime(utc, longitudeDeg);
        double lst = lstHours * Math.PI / 12.0;

        double H = lst - ra;
        double pi = Math.Asin(EarthRadiusAu / Math.Max(distanceAu, 1e-6));

        double dRa = -pi * Math.Cos(phi) * Math.Sin(H) / Math.Cos(dec);
        double dDec = -pi * (Math.Sin(phi) * Math.Cos(dec) - Math.Cos(phi) * Math.Cos(H) * Math.Sin(dec));

        double raTop = ra + dRa;
        double decTop = dec + dDec;

        double raHoursTop = (raTop * 12.0 / Math.PI) % 24.0;
        if (raHoursTop < 0)
        {
            raHoursTop += 24.0;
        }

        return (raHoursTop, AstronomyMath.RadiansToDegrees(decTop));
    }
}
