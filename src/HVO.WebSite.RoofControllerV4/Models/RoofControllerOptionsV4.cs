namespace HVO.WebSite.RoofControllerV4.Models;

public record class RoofControllerOptionsV4
{
    /// <summary>
    /// Maximum time the roof can run continuously in either direction before the safety watchdog stops it.
    /// This prevents runaway operations that could damage the roof or motors.
    /// </summary>
    public TimeSpan SafetyWatchdogTimeout { get; set; } = TimeSpan.FromSeconds(90);

    public int OpenRelayId { get; set; } = 1; // FWD
    public int CloseRelayId { get; set; } = 2; // REV
    public int ClearFault { get; set; } = 3;
    public int StopRelayId { get; set; } = 4;

    /// <summary>
    /// Enables background polling of the 4 digital inputs on the FourRelayFourInput HAT.
    /// When enabled, input edge-change events will be raised.
    /// </summary>
    public bool EnableDigitalInputPolling { get; set; } = true;

    /// <summary>
    /// Interval between input polls. Keep small for responsive edge notifications.
    /// </summary>
    public TimeSpan DigitalInputPollInterval { get; set; } = TimeSpan.FromMilliseconds(25);
}
