using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.HostedServices;
using HVO.SkyMonitorV5.RPi.Storage;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Exports;
using HVO.SkyMonitorV5.RPi.Services;
using HVO.SkyMonitorV5.RPi.ImageHistory;
using HVO.SkyMonitorV5.RPi.Telemetry;
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
        frameStateStore.Setup(store => store.UpdateProcessingQueueStatus(It.IsAny<ProcessingQueueStatus>()));
        frameStateStore.Setup(store => store.UpdateProcessingQueueStatus(It.IsAny<ProcessingQueueStatus>()));

        var clock = new Mock<IObservatoryClock>(MockBehavior.Strict);
        clock.SetupGet(c => c.UtcNow).Returns(() => DateTimeOffset.UtcNow);
        clock.SetupGet(c => c.LocalNow).Returns(() => DateTimeOffset.Now);
        clock.SetupGet(c => c.TimeZone).Returns(TimeZoneInfo.Utc);
        clock.SetupGet(c => c.TimeZoneDisplayName).Returns("UTC");
        clock.Setup(c => c.GetZoneLabel(It.IsAny<DateTimeOffset>())).Returns("UTC");
        clock.Setup(c => c.ToLocal(It.IsAny<DateTimeOffset>())).Returns<DateTimeOffset>(timestamp => timestamp);

        var dispatcher = new Mock<IFrameExportDispatcher>();
        dispatcher.Setup(d => d.TryEnqueue(It.IsAny<FrameExportEnvelope>())).Returns(true);
        var encoder = new Mock<IProcessedFrameEncoder>(MockBehavior.Strict);
        var exportPublisher = CreateExportPublisher(dispatcher.Object, encoder.Object);

        using var service = new BackgroundFrameStackerService(
            optionsMonitor.Object,
            frameStacker.Object,
            pipeline.Object,
            frameStateStore.Object,
            clock.Object,
            exportPublisher,
            NullLogger<BackgroundFrameStackerService>.Instance);

        frameStateStore.Invocations.Clear();

        var capturedStatuses = new List<BackgroundStackerStatus>();
        frameStateStore
            .Setup(store => store.UpdateBackgroundStackerStatus(It.IsAny<BackgroundStackerStatus>()))
            .Callback<BackgroundStackerStatus>(status => capturedStatuses.Add(status));

        var exposure = new ExposureSettings(ExposureMilliseconds: 1_000, Gain: 200, AutoExposure: false, AutoGain: false);
        var capture = new CapturedImage(Guid.NewGuid(), null!, DateTimeOffset.UtcNow, exposure, null);
        const long captureSizeBytes = 0;

        var workItem1 = new StackingWorkItem(
            FrameNumber: 41,
            Capture: capture,
            ConfigurationSnapshot: configuration,
            ConfigurationVersion: 1,
            EnqueuedAt: DateTimeOffset.UtcNow.AddMilliseconds(-25),
            CaptureSizeBytes: captureSizeBytes);

        var workItem2 = new StackingWorkItem(
            FrameNumber: 42,
            Capture: capture,
            ConfigurationSnapshot: configuration,
            ConfigurationVersion: 1,
            EnqueuedAt: DateTimeOffset.UtcNow,
            CaptureSizeBytes: captureSizeBytes);

        var method = typeof(BackgroundFrameStackerService)
            .GetMethod("RecordProcessingTelemetry", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method, "Expected non-public RecordProcessingTelemetry method to exist.");

        method.Invoke(service, new object[] { workItem1, 10.0, 25.0, 40.0 });
        method.Invoke(service, new object[] { workItem2, 18.0, 35.0, 55.0 });

        Assert.HasCount(2, capturedStatuses, "Telemetry should be published for each work item.");

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

        var clock = new Mock<IObservatoryClock>(MockBehavior.Strict);
        clock.SetupGet(c => c.UtcNow).Returns(() => DateTimeOffset.UtcNow);
        clock.SetupGet(c => c.LocalNow).Returns(() => DateTimeOffset.Now);
        clock.SetupGet(c => c.TimeZone).Returns(TimeZoneInfo.Utc);
        clock.SetupGet(c => c.TimeZoneDisplayName).Returns("UTC");
        clock.Setup(c => c.GetZoneLabel(It.IsAny<DateTimeOffset>())).Returns("UTC");
        clock.Setup(c => c.ToLocal(It.IsAny<DateTimeOffset>())).Returns<DateTimeOffset>(timestamp => timestamp);

        var dispatcher = new Mock<IFrameExportDispatcher>();
        dispatcher.Setup(d => d.TryEnqueue(It.IsAny<FrameExportEnvelope>())).Returns(true);
        var encoder = new Mock<IProcessedFrameEncoder>(MockBehavior.Strict);
        var exportPublisher = CreateExportPublisher(dispatcher.Object, encoder.Object);

        using var service = new BackgroundFrameStackerService(
            optionsMonitor.Object,
            frameStacker.Object,
            pipeline.Object,
            frameStateStore.Object,
            clock.Object,
            exportPublisher,
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

    [TestMethod]
    public async Task EnqueueAsync_RetriesAfterChannelSwapDuringWait()
    {
        var options = new CameraPipelineOptions
        {
            EnableStacking = true,
            EnableImageOverlays = false,
            BackgroundStacker = new BackgroundStackerOptions
            {
                Enabled = true,
                QueueCapacity = 2,
                OverflowPolicy = BackgroundStackerOverflowPolicy.Block,
                AdaptiveQueue = new AdaptiveQueueOptions
                {
                    Enabled = true,
                    MinCapacity = 2,
                    MaxCapacity = 6,
                    IncreaseStep = 2,
                    DecreaseStep = 1,
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
        frameStateStore.Setup(store => store.UpdateProcessingQueueStatus(It.IsAny<ProcessingQueueStatus>()));

        var dispatcher = new Mock<IFrameExportDispatcher>();
        dispatcher.Setup(d => d.TryEnqueue(It.IsAny<FrameExportEnvelope>())).Returns(true);
        var encoder = new Mock<IProcessedFrameEncoder>(MockBehavior.Strict);
        var exportPublisher = CreateExportPublisher(dispatcher.Object, encoder.Object);

        var clock = new Mock<IObservatoryClock>(MockBehavior.Strict);
        clock.SetupGet(c => c.UtcNow).Returns(() => DateTimeOffset.UtcNow);
        clock.SetupGet(c => c.LocalNow).Returns(() => DateTimeOffset.Now);
        clock.SetupGet(c => c.TimeZone).Returns(TimeZoneInfo.Utc);
        clock.SetupGet(c => c.TimeZoneDisplayName).Returns("UTC");
        clock.Setup(c => c.GetZoneLabel(It.IsAny<DateTimeOffset>())).Returns("UTC");
        clock.Setup(c => c.ToLocal(It.IsAny<DateTimeOffset>())).Returns<DateTimeOffset>(timestamp => timestamp);

        using var service = new BackgroundFrameStackerService(
            optionsMonitor.Object,
            frameStacker.Object,
            pipeline.Object,
            frameStateStore.Object,
            clock.Object,
            exportPublisher,
            NullLogger<BackgroundFrameStackerService>.Instance);

        var serviceType = typeof(BackgroundFrameStackerService);
        var channelField = serviceType.GetField("_channel", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(channelField, "Expected _channel field via reflection.");

        var onEnqueued = serviceType.GetMethod("OnWorkItemEnqueued", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(onEnqueued, "Expected OnWorkItemEnqueued method via reflection.");

        var exposure = new ExposureSettings(ExposureMilliseconds: 500, Gain: 150, AutoExposure: false, AutoGain: false);
        var configurationSnapshot = configuration;

        var frameCounter = 100;

        StackingWorkItem CreateWorkItem()
        {
            var frameNumber = ++frameCounter;
            return new StackingWorkItem(
                FrameNumber: frameNumber,
                Capture: new CapturedImage(Guid.NewGuid(), null!, DateTimeOffset.UtcNow, exposure, null),
                ConfigurationSnapshot: configurationSnapshot,
                ConfigurationVersion: 1,
                EnqueuedAt: DateTimeOffset.UtcNow,
                CaptureSizeBytes: 0);
        }

        var channel = (Channel<StackingWorkItem>)channelField.GetValue(service)!;
        for (var i = 0; i < options.BackgroundStacker.QueueCapacity; i++)
        {
            var seedItem = CreateWorkItem();
            Assert.IsTrue(channel.Writer.TryWrite(seedItem), "Expected initial channel to accept seeded items.");
            onEnqueued.Invoke(service, new object[] { seedItem });
        }

        var pendingItem = CreateWorkItem();
        var enqueueTask = Task.Run(async () => await service.EnqueueAsync(pendingItem, CancellationToken.None));

        await Task.Delay(50);

        var applyCapacity = serviceType.GetMethod("ApplyAdaptiveQueueCapacity", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(applyCapacity, "Expected ApplyAdaptiveQueueCapacity method via reflection.");
        applyCapacity.Invoke(service, new object[] { 4, "test" });

        var completed = await Task.WhenAny(enqueueTask, Task.Delay(1000));
        Assert.AreSame(enqueueTask, completed, "Enqueue should resume after channel swap.");

        var result = await enqueueTask;
        Assert.IsTrue(result, "Enqueue should succeed after adaptive channel replacement.");

        static void DrainChannel(Channel<StackingWorkItem>? target)
        {
            if (target is null)
            {
                return;
            }

            while (target.Reader.TryRead(out var item))
            {
                item.Capture.Image?.Dispose();
                item.Capture.Context?.Dispose();
            }
        }

        DrainChannel(channel);
        var currentChannel = (Channel<StackingWorkItem>)channelField.GetValue(service)!;
        if (!ReferenceEquals(channel, currentChannel))
        {
            DrainChannel(currentChannel);
        }
    }

    private static FrameExportPublisher CreateExportPublisher(IFrameExportDispatcher dispatcher, IProcessedFrameEncoder encoder)
    {
        var optionsMonitor = new Mock<IOptionsMonitor<SkiaPipelineFeatureOptions>>();
        optionsMonitor.SetupGet(o => o.CurrentValue).Returns(new SkiaPipelineFeatureOptions());
        var featureMonitor = new Mock<ISkiaPipelineFeatureToggleMonitor>();
        var archiveQueue = new Mock<IImageFrameArchiveIngestionQueue>();
        archiveQueue.Setup(q => q.TryEnqueue(It.IsAny<ImageFrameArchiveIngestionRequest>())).Returns(true);
        var imageHistoryOptions = new Mock<IOptionsMonitor<ImageHistoryOptions>>();
        imageHistoryOptions.SetupGet(o => o.CurrentValue).Returns(new ImageHistoryOptions());
        var exportOptions = new Mock<IOptionsMonitor<FrameExportOptions>>();
        exportOptions.SetupGet(o => o.CurrentValue).Returns(new FrameExportOptions());

        return new FrameExportPublisher(
            dispatcher,
            encoder,
            Mock.Of<IFitsFrameEncoder>(),
            NullLogger<FrameExportPublisher>.Instance,
            optionsMonitor.Object,
            featureMonitor.Object,
            archiveQueue.Object,
            imageHistoryOptions.Object,
            exportOptions.Object);
    }
}
