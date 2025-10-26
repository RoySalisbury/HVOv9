using System;
using System.Collections.Generic;

namespace HVO.SkyMonitorV5.RPi.Models;

public sealed record TelemetryEventLogEntry(
    long Id,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset OccurredAtLocal,
    string Category,
    string EventType,
    string Severity,
    string? Summary,
    string? Detail,
    string? PropertiesJson);

public sealed record TelemetryEventPage(
    DateTimeOffset GeneratedAtLocal,
    IReadOnlyList<TelemetryEventLogEntry> Events,
    long? LatestEventId,
    long? OldestEventId,
    bool HasMoreBefore,
    bool HasMoreAfter);
