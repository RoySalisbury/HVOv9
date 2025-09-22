namespace HVO.CLI.RoofController.Logic;

public record class RoofControllerOptionsV2
{
    /// <summary>
    /// Maximum time the roof can run continuously in either direction before the safety watchdog stops it.
    /// This prevents runaway operations that could damage the roof or motors.
    /// </summary>
    public TimeSpan SafetyWatchdogTimeout { get; set; } = TimeSpan.FromSeconds(90);

    public int StopRelayId { get; set; } = 1;
    public int OpenRelayId { get; set; } = 2;
    public int CloseRelayId { get; set; } = 3;

    public int MaxRelayRetryAttempts { get; set; } = 3;
    public int RelayRetryDelayMs { get; set; } = 5;
}
