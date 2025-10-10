using System;
using System.Collections.Generic;
using System.Reflection;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.HostedServices;
using HVO.SkyMonitorV5.RPi.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Tests.HostedServices;

[TestClass]
public sealed class BackgroundFrameStackerServicePerformanceTests
{
    [TestMethod]
    public void RecordProcessingTelemetry_AccumulatesStackAndFilterDurations()
    {
        var options = new CameraPipelineOptions
        {
            EnableStacking = true,
            EnableImageOverlays = true,
            BackgroundStacker = new BackgroundStackerOptions
            {
                Enabled = true,
                QueueCapacity = 8
            }
        };

        var configuration = CameraConfiguration.FromOptions(options);

        var optionsMonitor = new Mock<IOptionsMonitor<CameraPipelineOptions>>();
        optionsMonitor.SetupGet(monitor => monitor.CurrentValue).Returns(options);
        optionsMonitor
            .Setup(monitor => monitor.OnChange(It.IsAny<Action<CameraPipelineOptions, string?>>()))
            .Returns(Mock.Of<IDisposable>());

        var frameStacker = new Mock<IFrameStacker>(MockBehavior.Strict);
        var pipeline = new Mock<IFrameFilterPipeline>(MockBehavior.Strict);

        var frameStateStore = new Mock<IFrameStateStore>(MockBehavior.Strict);
        frameStateStore.SetupGet(store => store.ConfigurationVersion).Returns(1);
        frameStateStore.SetupGet(store => store.Configuration).Returns(configuration);
        frameStateStore.Setup(store => store.UpdateBackgroundStackerStatus(It.IsAny<BackgroundStackerStatus>()));

        using var service = new BackgroundFrameStackerService(
            optionsMonitor.Object,
            frameStacker.Object,
            pipeline.Object,
            frameStateStore.Object,
            NullLogger<BackgroundFrameStackerService>.Instance);

        frameStateStore.Invocations.Clear();

        var capturedStatuses = new List<BackgroundStackerStatus>();
        frameStateStore
            .Setup(store => store.UpdateBackgroundStackerStatus(It.IsAny<BackgroundStackerStatus>()))
            .Callback<BackgroundStackerStatus>(status => capturedStatuses.Add(status));

        using var bitmap = new SKBitmap(width: 4, height: 4);
        var exposure = new ExposureSettings(ExposureMilliseconds: 1_000, Gain: 200, AutoExposure: false, AutoGain: false);
    var capture = new CapturedImage(bitmap, DateTimeOffset.UtcNow, exposure, null);

        var workItem1 = new StackingWorkItem(
            FrameNumber: 41,
            Capture: capture,
            ConfigurationSnapshot: configuration,
            ConfigurationVersion: 1,
            EnqueuedAt: DateTimeOffset.UtcNow.AddMilliseconds(-25));

        var workItem2 = new StackingWorkItem(
            FrameNumber: 42,
            Capture: capture,
            ConfigurationSnapshot: configuration,
            ConfigurationVersion: 1,
            EnqueuedAt: DateTimeOffset.UtcNow);

        var method = typeof(BackgroundFrameStackerService)
            .GetMethod("RecordProcessingTelemetry", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method, "Expected non-public RecordProcessingTelemetry method to exist.");

        method.Invoke(service, new object[] { workItem1, 10.0, 25.0, 40.0 });
        method.Invoke(service, new object[] { workItem2, 18.0, 35.0, 55.0 });

        Assert.AreEqual(2, capturedStatuses.Count, "Telemetry should be published for each work item.");

        var latest = capturedStatuses[^1];
        Assert.AreEqual(2, latest.ProcessedFrameCount, "Processed frame count should accumulate.");
        Assert.AreEqual(42, latest.LastFrameNumber, "Last frame number should reflect most recent work item.");
    Assert.IsTrue(latest.LastQueueLatencyMilliseconds.HasValue, "Latest queue latency should be recorded.");
    Assert.AreEqual(18.0, latest.LastQueueLatencyMilliseconds.Value, 1e-3, "Last queue latency should match latest telemetry.");
    Assert.IsTrue(latest.LastStackMilliseconds.HasValue, "Latest stack duration should be recorded.");
    Assert.AreEqual(35.0, latest.LastStackMilliseconds.Value, 1e-3, "Last stack duration should match latest telemetry.");
    Assert.IsTrue(latest.LastFilterMilliseconds.HasValue, "Latest filter duration should be recorded.");
    Assert.AreEqual(55.0, latest.LastFilterMilliseconds.Value, 1e-3, "Last filter duration should match latest telemetry.");
    Assert.IsTrue(latest.AverageQueueLatencyMilliseconds.HasValue, "Average queue latency should be tracked.");
    Assert.AreEqual(14.0, latest.AverageQueueLatencyMilliseconds.Value, 1e-3, "Average queue latency should reflect all samples.");
    Assert.IsTrue(latest.AverageStackMilliseconds.HasValue, "Average stack duration should be tracked.");
    Assert.AreEqual(30.0, latest.AverageStackMilliseconds.Value, 1e-3, "Average stack duration should reflect all samples.");
    Assert.IsTrue(latest.AverageFilterMilliseconds.HasValue, "Average filter duration should be tracked.");
    Assert.AreEqual(47.5, latest.AverageFilterMilliseconds.Value, 1e-3, "Average filter duration should reflect all samples.");
    }

    [TestMethod]
    public void AdaptiveQueue_AdjustsCapacityForSustainedPressureChanges()
    {
        var options = new CameraPipelineOptions
        {
            EnableStacking = true,
            EnableImageOverlays = false,
            BackgroundStacker = new BackgroundStackerOptions
            {
                Enabled = true,
                QueueCapacity = 24,
                AdaptiveQueue = new AdaptiveQueueOptions
                {
                    Enabled = true,
                    MinCapacity = 16,
                    MaxCapacity = 40,
                    IncreaseStep = 4,
                    DecreaseStep = 4,
                    ScaleUpThresholdPercent = 70,
                    ScaleDownThresholdPercent = 30,
                    EvaluationWindowSeconds = 1,
                    CooldownSeconds = 1
                }
            }
        };

        var configuration = CameraConfiguration.FromOptions(options);

        var optionsMonitor = new Mock<IOptionsMonitor<CameraPipelineOptions>>();
        optionsMonitor.SetupGet(monitor => monitor.CurrentValue).Returns(options);
        optionsMonitor.Setup(monitor => monitor.OnChange(It.IsAny<Action<CameraPipelineOptions, string?>>()))
            .Returns(Mock.Of<IDisposable>());

        var frameStacker = new Mock<IFrameStacker>(MockBehavior.Strict);
        var pipeline = new Mock<IFrameFilterPipeline>(MockBehavior.Strict);

        var frameStateStore = new Mock<IFrameStateStore>(MockBehavior.Strict);
        frameStateStore.SetupGet(store => store.ConfigurationVersion).Returns(1);
        frameStateStore.SetupGet(store => store.Configuration).Returns(configuration);
        frameStateStore.Setup(store => store.UpdateBackgroundStackerStatus(It.IsAny<BackgroundStackerStatus>()));

        using var service = new BackgroundFrameStackerService(
            optionsMonitor.Object,
            frameStacker.Object,
            pipeline.Object,
            frameStateStore.Object,
            NullLogger<BackgroundFrameStackerService>.Instance);

        var serviceType = typeof(BackgroundFrameStackerService);
        var updateQueuePressure = serviceType.GetMethod("UpdateQueuePressure", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(updateQueuePressure, "Expected UpdateQueuePressure method via reflection.");

        static void SetField<T>(BackgroundFrameStackerService target, string fieldName, T value)
        {
            var field = typeof(BackgroundFrameStackerService).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Expected field '{fieldName}'");
            field.SetValue(target, value);
        }

        void ForceAdaptiveWindow()
        {
            SetField(service, "_lastAdaptiveSampleTimestamp", DateTimeOffset.UtcNow - TimeSpan.FromSeconds(2));
            SetField(service, "_adaptiveNextAdjustmentAllowed", DateTimeOffset.UtcNow - TimeSpan.FromSeconds(1));
        }

        ForceAdaptiveWindow();
        updateQueuePressure.Invoke(service, new object[] { 28 });

        var currentOptionsField = serviceType.GetField("_currentOptions", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(currentOptionsField, "Expected _currentOptions field via reflection.");
        var currentOptions = (BackgroundStackerOptions)currentOptionsField.GetValue(service)!;
        Assert.AreEqual(28, currentOptions.QueueCapacity, "Sustained high pressure should increase queue capacity within bounds.");

        ForceAdaptiveWindow();
        updateQueuePressure.Invoke(service, new object[] { 4 });

        currentOptions = (BackgroundStackerOptions)currentOptionsField.GetValue(service)!;
        Assert.AreEqual(24, currentOptions.QueueCapacity, "Sustained low pressure should decrease queue capacity within bounds.");
    }
}
