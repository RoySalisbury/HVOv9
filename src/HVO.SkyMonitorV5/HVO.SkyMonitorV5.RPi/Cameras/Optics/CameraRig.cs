namespace HVO.SkyMonitorV5.RPi.Cameras.Optics;


public sealed record CameraRig(
    SensorSpec Sensor,
    LensSpec Lens,
    double BoresightAltDeg,   // where the camera points (Alt/Az)
    double BoresightAzDeg);
