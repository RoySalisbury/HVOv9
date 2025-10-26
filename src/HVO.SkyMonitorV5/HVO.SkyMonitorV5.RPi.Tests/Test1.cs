using HVO.SkyMonitorV5.RPi.Models;

namespace HVO.SkyMonitorV5.RPi.Tests;

internal static class DiagnosticsTestData
{
    public static BackgroundStackerStatus CreateBackgroundStackerStatus(
        bool enabled = true,
        int queueDepth = 5,
        int queueCapacity = 10,
        int peakQueueDepth = 8,
        long processedFrameCount = 1_000,
        long droppedFrameCount = 5,
        int? lastFrameNumber = 250,
        DateTimeOffset? lastEnqueuedAt = null,
        DateTimeOffset? lastCompletedAt = null,
        double? lastQueueLatencyMilliseconds = 200.0,
        double? averageQueueLatencyMilliseconds = 175.0,
        double? maxQueueLatencyMilliseconds = 400.0,
        double? lastStackMilliseconds = 320.0,
        double? lastFilterMilliseconds = 120.0,
        double? averageStackMilliseconds = 300.0,
        double? averageFilterMilliseconds = 110.0,
        long queueMemoryBytes = 2_048,
        long peakQueueMemoryBytes = 4_096,
        double queueFillPercentage = 55.0,
        double peakQueueFillPercentage = 80.0,
        double queueMemoryMegabytes = 2.0,
        double peakQueueMemoryMegabytes = 4.0,
        double? secondsSinceLastCompleted = 1.5,
        int queuePressureLevel = 2)
    {
        lastEnqueuedAt ??= DateTimeOffset.UtcNow;
        lastCompletedAt ??= lastEnqueuedAt.Value.AddSeconds(-2);

        return new BackgroundStackerStatus(
            enabled,
            queueDepth,
            queueCapacity,
            peakQueueDepth,
            processedFrameCount,
            droppedFrameCount,
            lastFrameNumber,
            lastEnqueuedAt,
            lastCompletedAt,
            lastQueueLatencyMilliseconds,
            averageQueueLatencyMilliseconds,
            maxQueueLatencyMilliseconds,
            lastStackMilliseconds,
            lastFilterMilliseconds,
            averageStackMilliseconds,
            averageFilterMilliseconds,
            queueMemoryBytes,
            peakQueueMemoryBytes,
            queueFillPercentage,
            peakQueueFillPercentage,
            queueMemoryMegabytes,
            peakQueueMemoryMegabytes,
            secondsSinceLastCompleted,
            queuePressureLevel);
    }
}
