using System;

namespace HVO.SkyMonitorV5.RPi.Models;

public enum RemoteDispatchOutcome
{
    Disabled,
    Succeeded,
    Skipped,
    Failed
}

public sealed record RemoteDispatchStatus(
    DateTimeOffset Timestamp,
    string Mode,
    RemoteDispatchOutcome Outcome,
    DateTimeOffset CapturedAtLocal,
    string? Message,
    string? ErrorMessage,
    RemoteDispatchMetricsSnapshot? Metrics = null);
