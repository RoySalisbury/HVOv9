namespace HVO.WebSite.RoofControllerV4.Models
{
    // Response wrapper for roof status with extended telemetry
    public sealed record RoofStatusResponse(
        RoofControllerStatus Status,
        bool IsMoving,
        RoofControllerStopReason LastStopReason,
        DateTimeOffset? LastTransitionUtc,
        bool IsWatchdogActive,
        double? WatchdogSecondsRemaining,
    bool IsAtSpeed);
}
