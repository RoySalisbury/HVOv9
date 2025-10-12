using System;

namespace HVO.SkyMonitorV5.RPi.Telemetry;

internal interface ITelemetrySystemProfileCollector
{
    TelemetrySystemProfileSnapshot Collect(DateTimeOffset observedAtUtc);
}
