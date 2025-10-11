using System;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Services;
using HVO.SkyMonitorV5.RPi.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace HVO.SkyMonitorV5.RPi.Tests.Services;

[TestClass]
public sealed class DiagnosticsServiceTests
{
    [TestMethod]
    public async Task GetBackgroundStackerMetricsAsync_ReturnsDefaultSnapshot_WhenStatusUnavailable()
    {
        var frameStateStore = new Mock<IFrameStateStore>();
        frameStateStore.SetupGet(s => s.BackgroundStackerStatus).Returns((BackgroundStackerStatus?)null);

        var pipeline = new Mock<IFrameFilterPipeline>();

        var service = CreateService(frameStateStore.Object, pipeline.Object);

        var result = await service.GetBackgroundStackerMetricsAsync();

        Assert.IsTrue(result.IsSuccessful, "Expected successful result when telemetry is unavailable.");

        var metrics = result.Value;
        Assert.IsFalse(metrics.Enabled, "Empty snapshot should indicate disabled stacker.");
        Assert.AreEqual(0, metrics.QueueDepth);
        Assert.AreEqual(0, metrics.QueueCapacity);
        Assert.AreEqual(0, metrics.QueueFillPercentage);
        Assert.IsNull(metrics.LastCompletedAt);
        Assert.IsNull(metrics.LastFrameNumber);
    }

    [TestMethod]
    public async Task GetBackgroundStackerMetricsAsync_UsesLastFrameTimestampFallback()
    {
        var lastFrameLocal = new DateTimeOffset(2025, 10, 10, 12, 0, 0, TimeSpan.Zero);
        var nowLocal = lastFrameLocal.AddMilliseconds(2345);

        var frameStateStore = new Mock<IFrameStateStore>();
        frameStateStore.SetupGet(s => s.BackgroundStackerStatus).Returns((BackgroundStackerStatus?)null);
        frameStateStore.SetupGet(s => s.LastFrameTimestamp).Returns(lastFrameLocal);

    var pipeline = new Mock<IFrameFilterPipeline>();

    var clock = new Mock<IObservatoryClock>();
    clock.SetupGet(c => c.UtcNow).Returns(nowLocal);
    clock.SetupGet(c => c.LocalNow).Returns(nowLocal);
    clock.SetupGet(c => c.TimeZone).Returns(TimeZoneInfo.Utc);
    clock.SetupGet(c => c.TimeZoneDisplayName).Returns("UTC");
    clock.Setup(c => c.ToLocal(It.IsAny<DateTimeOffset>())).Returns<DateTimeOffset>(timestamp => timestamp);
    clock.Setup(c => c.GetZoneLabel(It.IsAny<DateTimeOffset>())).Returns("UTC");

    var service = CreateService(frameStateStore.Object, pipeline.Object, clock);

        var result = await service.GetBackgroundStackerMetricsAsync();

        Assert.IsTrue(result.IsSuccessful, "Fallback snapshot should succeed.");

        var metrics = result.Value;
        Assert.AreEqual(lastFrameLocal, metrics.LastCompletedAt, "Fallback should surface last frame timestamp.");
        Assert.IsNotNull(metrics.SecondsSinceLastCompleted, "Fallback should compute elapsed seconds.");
        Assert.AreEqual(2.345, metrics.SecondsSinceLastCompleted.Value, 0.0005, "Elapsed seconds should include millisecond precision.");
    }

    [TestMethod]
    public async Task GetBackgroundStackerMetricsAsync_MapsTelemetryFields()
    {
        var completedUtc = new DateTimeOffset(2025, 10, 10, 11, 58, 0, TimeSpan.Zero);
        const double expectedSeconds = 2.5;
        var nowUtc = completedUtc.AddSeconds(expectedSeconds);

        var status = new BackgroundStackerStatus(
            Enabled: true,
            QueueDepth: 5,
            QueueCapacity: 10,
            PeakQueueDepth: 8,
            ProcessedFrameCount: 1234,
            DroppedFrameCount: 12,
            LastFrameNumber: 321,
            LastEnqueuedAt: nowUtc,
            LastCompletedAt: completedUtc,
            LastQueueLatencyMilliseconds: 220.5,
            AverageQueueLatencyMilliseconds: 180.2,
            MaxQueueLatencyMilliseconds: 450.0,
            LastStackMilliseconds: 340.1,
            LastFilterMilliseconds: 95.4,
            AverageStackMilliseconds: 310.7,
            AverageFilterMilliseconds: 88.6,
            QueueMemoryBytes: 2_097_152,
            PeakQueueMemoryBytes: 3_145_728,
            QueueFillPercentage: 55.5,
            PeakQueueFillPercentage: 80.0,
            QueueMemoryMegabytes: 2.0,
            PeakQueueMemoryMegabytes: 3.0,
            SecondsSinceLastCompleted: expectedSeconds,
            QueuePressureLevel: 3);

        var clock = new Mock<IObservatoryClock>();
        clock.SetupGet(c => c.UtcNow).Returns(nowUtc);
        clock.SetupGet(c => c.LocalNow).Returns(nowUtc);
        clock.SetupGet(c => c.TimeZone).Returns(TimeZoneInfo.Utc);
        clock.SetupGet(c => c.TimeZoneDisplayName).Returns("UTC");
        clock.Setup(c => c.ToLocal(It.IsAny<DateTimeOffset>())).Returns<DateTimeOffset>(timestamp => timestamp);
        clock.Setup(c => c.GetZoneLabel(It.IsAny<DateTimeOffset>())).Returns("UTC");

        var frameStateStore = new Mock<IFrameStateStore>();
        frameStateStore.SetupGet(s => s.BackgroundStackerStatus).Returns(status);

        var pipeline = new Mock<IFrameFilterPipeline>();

        var service = CreateService(frameStateStore.Object, pipeline.Object, clock);

        var result = await service.GetBackgroundStackerMetricsAsync();

        Assert.IsTrue(result.IsSuccessful, "Stacker telemetry should be mapped successfully.");

        var metrics = result.Value;
        Assert.IsTrue(metrics.Enabled);
        Assert.AreEqual(status.QueueDepth, metrics.QueueDepth);
        Assert.AreEqual(status.QueueCapacity, metrics.QueueCapacity);
        Assert.AreEqual(status.PeakQueueDepth, metrics.PeakQueueDepth);
        Assert.AreEqual(status.ProcessedFrameCount, metrics.ProcessedFrameCount);
        Assert.AreEqual(status.DroppedFrameCount, metrics.DroppedFrameCount);
        Assert.AreEqual(status.QueuePressureLevel, metrics.QueuePressureLevel);
        Assert.AreEqual(status.QueueFillPercentage, metrics.QueueFillPercentage);
        Assert.AreEqual(status.PeakQueueFillPercentage, metrics.PeakQueueFillPercentage);
        Assert.AreEqual(status.QueueMemoryMegabytes, metrics.QueueMemoryMegabytes);
        Assert.AreEqual(status.PeakQueueMemoryMegabytes, metrics.PeakQueueMemoryMegabytes);
        Assert.AreEqual(status.LastEnqueuedAt, metrics.LastEnqueuedAt);
    Assert.AreEqual(status.LastCompletedAt, metrics.LastCompletedAt);
    Assert.IsNotNull(metrics.SecondsSinceLastCompleted);
    Assert.AreEqual(expectedSeconds, metrics.SecondsSinceLastCompleted!.Value, 0.0001);
        Assert.AreEqual(status.LastFrameNumber, metrics.LastFrameNumber);
        Assert.AreEqual(status.LastQueueLatencyMilliseconds, metrics.LastQueueLatencyMilliseconds);
        Assert.AreEqual(status.AverageQueueLatencyMilliseconds, metrics.AverageQueueLatencyMilliseconds);
        Assert.AreEqual(status.MaxQueueLatencyMilliseconds, metrics.MaxQueueLatencyMilliseconds);
        Assert.AreEqual(status.LastStackMilliseconds, metrics.LastStackMilliseconds);
        Assert.AreEqual(status.AverageStackMilliseconds, metrics.AverageStackMilliseconds);
        Assert.AreEqual(status.LastFilterMilliseconds, metrics.LastFilterMilliseconds);
        Assert.AreEqual(status.AverageFilterMilliseconds, metrics.AverageFilterMilliseconds);
    }

    [TestMethod]
    public async Task GetFilterMetricsAsync_UsesPipelineTelemetrySnapshot()
    {
        var frameStateStore = new Mock<IFrameStateStore>();

        var pipeline = new FrameFilterPipeline(
            Array.Empty<HVO.SkyMonitorV5.RPi.Pipeline.Filters.IFrameFilter>(),
            NullLogger<FrameFilterPipeline>.Instance);

        var service = CreateService(frameStateStore.Object, pipeline);

        var result = await service.GetFilterMetricsAsync();

        Assert.IsTrue(result.IsSuccessful, "Expected successful snapshot retrieval.");
        Assert.AreEqual(0, result.Value.Filters.Count, "New pipeline telemetry should start empty.");
    }

    [TestMethod]
    public async Task GetFilterMetricsAsync_ReturnsEmptySnapshot_WhenPipelineDoesNotExposeTelemetry()
    {
        var frameStateStore = new Mock<IFrameStateStore>();
        var pipeline = new Mock<IFrameFilterPipeline>();

        var service = CreateService(frameStateStore.Object, pipeline.Object);

        var result = await service.GetFilterMetricsAsync();

        Assert.IsTrue(result.IsSuccessful, "Fallback pipeline should still succeed.");
        Assert.AreEqual(0, result.Value.Filters.Count, "Fallback snapshot should be empty.");
    }

    [TestMethod]
    public async Task GetSystemDiagnosticsAsync_ReturnsSnapshot()
    {
        var frameStateStore = new Mock<IFrameStateStore>();
        var pipeline = new Mock<IFrameFilterPipeline>();

        var service = CreateService(frameStateStore.Object, pipeline.Object);

        var result = await service.GetSystemDiagnosticsAsync();

        Assert.IsTrue(result.IsSuccessful, "System diagnostics should resolve successfully.");

        var snapshot = result.Value;
        Assert.IsNotNull(snapshot);
        Assert.IsTrue(snapshot.ThreadCount >= 0, "Thread count should be non-negative.");
        Assert.IsTrue(snapshot.ProcessWorkingSetMegabytes >= 0, "Working set should be non-negative.");
    }

    private static DiagnosticsService CreateService(
        IFrameStateStore frameStateStore,
        IFrameFilterPipeline pipeline,
        Mock<IObservatoryClock>? clockMock = null)
    {
        var clock = clockMock ?? CreateDefaultClockMock();

        return new DiagnosticsService(frameStateStore, pipeline, NullLogger<DiagnosticsService>.Instance, clock.Object);
    }

    private static Mock<IObservatoryClock> CreateDefaultClockMock()
    {
        var clock = new Mock<IObservatoryClock>();
        clock.SetupGet(c => c.UtcNow).Returns(() => DateTimeOffset.UtcNow);
        clock.SetupGet(c => c.LocalNow).Returns(() => DateTimeOffset.Now);
        clock.SetupGet(c => c.TimeZone).Returns(TimeZoneInfo.Utc);
        clock.SetupGet(c => c.TimeZoneDisplayName).Returns("UTC");
        clock.Setup(c => c.ToLocal(It.IsAny<DateTimeOffset>())).Returns<DateTimeOffset>(timestamp => timestamp);
        clock.Setup(c => c.GetZoneLabel(It.IsAny<DateTimeOffset>())).Returns("UTC");
        return clock;
    }
}
