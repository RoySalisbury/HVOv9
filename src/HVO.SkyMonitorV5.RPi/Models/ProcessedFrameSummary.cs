namespace HVO.SkyMonitorV5.RPi.Models;

/// <summary>
/// Provides lightweight metadata about the latest processed frame for UI/reporting purposes.
/// </summary>
public sealed record ProcessedFrameSummary(int FramesStacked, int IntegrationMilliseconds);
