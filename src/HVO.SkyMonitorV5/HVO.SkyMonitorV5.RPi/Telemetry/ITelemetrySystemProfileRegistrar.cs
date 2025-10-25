using System;
using System.Threading;
using System.Threading.Tasks;

namespace HVO.SkyMonitorV5.RPi.Telemetry;

internal interface ITelemetrySystemProfileRegistrar
{
    Task RegisterAsync(DateTimeOffset observedAtUtc, CancellationToken cancellationToken);
}
