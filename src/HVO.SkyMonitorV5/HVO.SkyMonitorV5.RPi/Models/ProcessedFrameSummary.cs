using System;
using System.Collections.Generic;
using HVO.SkyMonitorV5.RPi.Pipeline.Composition;

namespace HVO.SkyMonitorV5.RPi.Models;

/// <summary>
/// Provides lightweight metadata about the latest processed frame for UI/reporting purposes.
/// </summary>
public sealed record ProcessedFrameSummary(
	int FramesStacked,
	int IntegrationMilliseconds,
	IReadOnlyList<string> AppliedFilters,
	int ProcessingMilliseconds)
{
	public double SurfaceMilliseconds { get; init; }
	public IReadOnlyList<FilterExecution> FilterExecutions { get; init; } = Array.Empty<FilterExecution>();
}
