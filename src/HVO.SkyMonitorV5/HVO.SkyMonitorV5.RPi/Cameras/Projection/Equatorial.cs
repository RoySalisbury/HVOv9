namespace HVO.SkyMonitorV5.RPi.Projection;

public static class Equatorial
{
    public static void RaDecToAltAz(double raHours, double decDeg, double lstHours, double latitudeDeg,
                                    out double altitudeDeg, out double azimuthDeg)
    {
        var hourAngle = (lstHours - raHours) * 15.0 * Math.PI / 180.0;
        var dec = decDeg * Math.PI / 180.0;
        var lat = latitudeDeg * Math.PI / 180.0;

        var sinAlt = Math.Sin(dec) * Math.Sin(lat) + Math.Cos(dec) * Math.Cos(lat) * Math.Cos(hourAngle);
        var alt = Math.Asin(Math.Clamp(sinAlt, -1.0, 1.0));

        var cosAz = (Math.Sin(dec) - Math.Sin(alt) * Math.Sin(lat)) / (Math.Cos(alt) * Math.Cos(lat));
        cosAz = Math.Clamp(cosAz, -1.0, 1.0);
        var az = Math.Acos(cosAz);
        if (Math.Sin(hourAngle) > 0) az = 2.0 * Math.PI - az;

        altitudeDeg = alt * 180.0 / Math.PI;
        azimuthDeg  = az  * 180.0 / Math.PI;
    }
}
