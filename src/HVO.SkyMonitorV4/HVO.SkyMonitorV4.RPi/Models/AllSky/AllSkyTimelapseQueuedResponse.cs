#nullable enable

namespace HVO.SkyMonitorV4.RPi.Models.AllSky;

public sealed record AllSkyTimelapseQueuedResponse(
    DateTimeOffset StartTimeUtc,
    DateTimeOffset EndTimeUtc,
    string OutputPrefix);
