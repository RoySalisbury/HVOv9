using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Services;
using HVO.SkyMonitorV5.RPi.Storage;
using HVO.SkyMonitorV5.RPi.Telemetry;
using HVO.SkyMonitorV5.RPi.Exports;
using HVO.SkyMonitorV5.Data.Abstractions;
using HVO.SkyMonitorV5.Data.Configurations;
using HVO.SkyMonitorV5.Data.Telemetry;
using HVO.SkyMonitorV5.Data.Telemetry.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    public async Task GetFrameExportMetricsAsync_ReturnsEmptySnapshot_WhenNoAttemptsRecorded()
    {
        var frameStateStore = new Mock<IFrameStateStore>();
        var pipeline = new Mock<IFrameFilterPipeline>();

        var service = CreateService(frameStateStore.Object, pipeline.Object);

        var result = await service.GetFrameExportMetricsAsync();

        Assert.IsTrue(result.IsSuccessful, "Diagnostics should succeed even when no export telemetry exists.");

        var snapshot = result.Value;
        Assert.AreEqual(0, snapshot.TotalAttemptCount, "Empty snapshot should report zero attempts.");
        Assert.AreEqual(0, snapshot.TotalSuccessCount, "Empty snapshot should report zero successes.");
        Assert.AreEqual(0, snapshot.Sinks.Count, "No sinks should be returned when telemetry is absent.");
        Assert.AreEqual(0d, snapshot.SuccessRatePercent, "Success rate should be zero with no attempts.");
    Assert.AreEqual(0, snapshot.PendingRetries.Count, "Empty snapshot should not include pending retries.");
    }

    [TestMethod]
    public async Task GetFrameExportMetricsAsync_AggregatesAttemptsPerSink()
    {
        var frameStateStore = new Mock<IFrameStateStore>();
        var pipeline = new Mock<IFrameFilterPipeline>();

        var baselineUtc = new DateTimeOffset(2025, 10, 12, 5, 0, 0, TimeSpan.Zero);

        var telemetryFactory = new TestDbContextFactory<SkyMonitorTelemetryContext>(() =>
        {
            var context = CreateInMemoryTelemetryContext();

            context.FrameExportAttempts.AddRange(new[]
            {
                new FrameExportAttemptEntity
                {
                    AttemptedAtUtc = baselineUtc.AddMinutes(1),
                    AttemptedAtLocal = baselineUtc.AddMinutes(1),
                    FrameId = Guid.NewGuid(),
                    Stage = (int)FrameExportStage.Raw,
                    SinkName = "raw-s3",
                    Success = true,
                    LatencyMilliseconds = 120,
                    QueueLatencyMilliseconds = 35,
                    ProcessingMilliseconds = 40,
                    FullPipelineMilliseconds = 5120,
                    PayloadBytes = 512_000,
                    PayloadContentType = "application/vnd.hvo.skia.raw",
                    PayloadExtension = ".skimg",
                    FramesStacked = 1,
                    IntegrationMilliseconds = 5000
                },
                new FrameExportAttemptEntity
                {
                    AttemptedAtUtc = baselineUtc.AddMinutes(2),
                    AttemptedAtLocal = baselineUtc.AddMinutes(2),
                    FrameId = Guid.NewGuid(),
                    Stage = (int)FrameExportStage.Raw,
                    SinkName = "raw-s3",
                    Success = false,
                    LatencyMilliseconds = 250,
                    QueueLatencyMilliseconds = 60,
                    ProcessingMilliseconds = 55,
                    FullPipelineMilliseconds = 5500,
                    PayloadBytes = 480_000,
                    PayloadContentType = "application/vnd.hvo.skia.raw",
                    PayloadExtension = "skimg",
                    ErrorMessage = "Upload failed"
                },
                new FrameExportAttemptEntity
                {
                    AttemptedAtUtc = baselineUtc.AddMinutes(3),
                    AttemptedAtLocal = baselineUtc.AddMinutes(3),
                    FrameId = Guid.NewGuid(),
                    Stage = (int)FrameExportStage.Processed,
                    SinkName = "processed-s3",
                    Success = true,
                    LatencyMilliseconds = 300,
                    QueueLatencyMilliseconds = 45,
                    ProcessingMilliseconds = 110,
                    FullPipelineMilliseconds = 18750,
                    PayloadBytes = 1_200_000,
                    PayloadContentType = "image/jpeg",
                    PayloadExtension = ".jpg",
                    FramesStacked = 8,
                    IntegrationMilliseconds = 18000
                }
            });

            context.SaveChanges();
            return context;
        });

        var service = CreateService(
            frameStateStore.Object,
            pipeline.Object,
            telemetryContextFactory: telemetryFactory);

        var result = await service.GetFrameExportMetricsAsync();

        Assert.IsTrue(result.IsSuccessful, "Frame export metrics should be aggregated successfully.");

        var snapshot = result.Value;
        Assert.AreEqual(3, snapshot.TotalAttemptCount, "Total attempt count should include all attempts.");
        Assert.AreEqual(2, snapshot.TotalSuccessCount, "Total success count should reflect successful attempts.");
        Assert.AreEqual(1, snapshot.TotalFailureCount, "Total failure count should reflect failed attempts.");
        Assert.AreEqual(2, snapshot.Sinks.Count, "Each stage/sink pair should produce a summary row.");
    Assert.AreEqual(0, snapshot.PendingRetries.Count, "No retries should be returned when none exist.");

        var rawSink = snapshot.Sinks.Single(s => s.SinkName == "raw-s3" && s.Stage == FrameExportStage.Raw);
        Assert.AreEqual(2, rawSink.AttemptCount, "Raw sink should aggregate both attempts.");
        Assert.AreEqual(1, rawSink.SuccessCount, "Raw sink success count should match successful attempts.");
        Assert.AreEqual(1, rawSink.FailureCount, "Raw sink failure count should match failed attempts.");
        Assert.IsTrue(rawSink.LastAttemptSucceeded.HasValue && !rawSink.LastAttemptSucceeded.Value, "Last attempt should expose success flag.");
        Assert.AreEqual("Upload failed", rawSink.LastFailureMessage, "Failure message should flow through telemetry snapshot.");
    Assert.IsNotNull(rawSink.LastAttemptLatencyMilliseconds, "Last attempt latency should be populated.");
    Assert.AreEqual(250d, rawSink.LastAttemptLatencyMilliseconds!.Value, 0.001, "Last attempt latency should be mapped.");
    Assert.IsNotNull(rawSink.LastAttemptQueueLatencyMilliseconds, "Last attempt queue latency should be populated.");
    Assert.AreEqual(60d, rawSink.LastAttemptQueueLatencyMilliseconds!.Value, 0.001, "Last attempt queue latency should be mapped.");
    Assert.IsNotNull(rawSink.LastAttemptProcessingMilliseconds, "Last attempt processing latency should be populated.");
    Assert.AreEqual(55d, rawSink.LastAttemptProcessingMilliseconds!.Value, 0.001, "Last attempt processing time should be mapped.");
    Assert.IsNotNull(rawSink.LastAttemptFullPipelineMilliseconds, "Last attempt full pipeline duration should be visible.");
    Assert.AreEqual(5500d, rawSink.LastAttemptFullPipelineMilliseconds!.Value, 0.001, "Full pipeline duration should reflect last attempt.");
    Assert.AreEqual(baselineUtc.AddMinutes(2), rawSink.LastFailureAtUtc, "Failure timestamp should match latest raw attempt.");
    Assert.AreEqual(baselineUtc.AddMinutes(2), rawSink.LastAttemptAtLocal, "Local timestamp should map latest attempt.");

        var processedSink = snapshot.Sinks.Single(s => s.Stage == FrameExportStage.Processed);
        Assert.AreEqual(1, processedSink.AttemptCount, "Processed sink should include single attempt.");
        Assert.AreEqual(1, processedSink.SuccessCount, "Processed sink should count success.");
        Assert.IsNull(processedSink.LastFailureMessage, "Processed sink should not report failure message for successful attempts.");
    Assert.IsNotNull(processedSink.AverageLatencyMilliseconds, "Average latency should be calculated for the processed sink.");
    Assert.AreEqual(300d, processedSink.AverageLatencyMilliseconds!.Value, 0.001, "Average latency should match lone attempt.");
    Assert.IsNotNull(processedSink.AverageFullPipelineMilliseconds, "Average full pipeline duration should be calculated.");
    Assert.AreEqual(18750d, processedSink.AverageFullPipelineMilliseconds!.Value, 0.001, "Average full pipeline duration should flow through telemetry.");

        Assert.AreEqual(66.67d, snapshot.SuccessRatePercent, 0.01, "Overall success rate should round to two decimals.");
        Assert.AreEqual(0, snapshot.PendingRetries.Count, "No pending retries should be returned when none exist.");
    }

    [TestMethod]
    public async Task GetFrameExportMetricsAsync_ReturnsPendingRetryPreview()
    {
        var frameStateStore = new Mock<IFrameStateStore>();
        var pipeline = new Mock<IFrameFilterPipeline>();

        var baselineUtc = new DateTimeOffset(2025, 10, 12, 8, 0, 0, TimeSpan.Zero);

        var telemetryFactory = new TestDbContextFactory<SkyMonitorTelemetryContext>(() =>
        {
            var context = CreateInMemoryTelemetryContext();

            for (var index = 0; index < 12; index++)
            {
                context.FrameExportRetries.Add(new FrameExportRetryEntity
                {
                    FrameId = Guid.NewGuid(),
                    Stage = (int)FrameExportStage.Raw,
                    SinkName = "raw-s3",
                    AttemptCount = index + 1,
                    EnqueuedAtUtc = baselineUtc.AddMinutes(-index * 2),
                    LastAttemptAtUtc = baselineUtc.AddMinutes(-index),
                    NextAttemptAtUtc = baselineUtc.AddMinutes(index),
                    LastErrorMessage = $"Error {index:00}"
                });
            }

            context.SaveChanges();
            return context;
        });

        var service = CreateService(frameStateStore.Object, pipeline.Object, telemetryContextFactory: telemetryFactory);

        var result = await service.GetFrameExportMetricsAsync();

        Assert.IsTrue(result.IsSuccessful, "Frame export metrics should succeed.");
        var snapshot = result.Value;

        Assert.AreEqual(12, snapshot.PendingRetryCount, "Pending retry count should reflect total queue depth.");
        Assert.AreEqual(10, snapshot.PendingRetries.Count, "Diagnostics should include a preview of pending retries.");

        var first = snapshot.PendingRetries.First();
        var last = snapshot.PendingRetries.Last();
        Assert.IsTrue(first.NextAttemptAtUtc <= last.NextAttemptAtUtc, "Preview list should be ordered by next attempt time.");
        Assert.IsNotNull(first.LastErrorMessage, "Entries should surface last error message.");
    }

    [TestMethod]
    public async Task GetFrameExportHistoryAsync_ReturnsAttemptsOrderedByLocalTimestamp()
    {
        var frameStateStore = new Mock<IFrameStateStore>();
        var pipeline = new Mock<IFrameFilterPipeline>();

        var baselineUtc = new DateTimeOffset(2025, 10, 12, 6, 0, 0, TimeSpan.Zero);

        var telemetryFactory = new TestDbContextFactory<SkyMonitorTelemetryContext>(() =>
        {
            var context = CreateInMemoryTelemetryContext();

            context.FrameExportAttempts.AddRange(new[]
            {
                new FrameExportAttemptEntity
                {
                    AttemptedAtUtc = baselineUtc.AddMinutes(5),
                    AttemptedAtLocal = baselineUtc.AddMinutes(10),
                    FrameId = Guid.NewGuid(),
                    Stage = (int)FrameExportStage.Processed,
                    SinkName = "processed-s3",
                    Success = true,
                    LatencyMilliseconds = 180,
                    QueueLatencyMilliseconds = 40,
                    ProcessingMilliseconds = 85,
                    FullPipelineMilliseconds = 6350
                },
                new FrameExportAttemptEntity
                {
                    AttemptedAtUtc = baselineUtc.AddMinutes(2),
                    AttemptedAtLocal = baselineUtc.AddMinutes(2),
                    FrameId = Guid.NewGuid(),
                    Stage = (int)FrameExportStage.Raw,
                    SinkName = "raw-s3",
                    Success = false,
                    LatencyMilliseconds = 260,
                    QueueLatencyMilliseconds = 75,
                    ProcessingMilliseconds = 95,
                    FullPipelineMilliseconds = 7200,
                    ErrorMessage = "Timeout"
                }
            });

            context.SaveChanges();
            return context;
        });

        var service = CreateService(
            frameStateStore.Object,
            pipeline.Object,
            telemetryContextFactory: telemetryFactory);

        var result = await service.GetFrameExportHistoryAsync();

        Assert.IsTrue(result.IsSuccessful, "Frame export history should be retrieved successfully.");

        var history = result.Value.Attempts;
        Assert.AreEqual(2, history.Count, "History should include all attempts.");

        Assert.IsTrue(history[0].AttemptedAtLocal <= history[1].AttemptedAtLocal, "Attempts should be sorted by local timestamp ascending.");
        Assert.AreEqual("Timeout", history[0].ErrorMessage, "The earliest attempt should expose its error details.");
        Assert.IsTrue(history[1].Success, "The latest attempt should retain success state.");
    Assert.IsNotNull(history[0].FullPipelineMilliseconds, "Failed attempt should capture full pipeline duration.");
    Assert.IsNotNull(history[1].FullPipelineMilliseconds, "Successful attempt should capture full pipeline duration.");
    Assert.AreEqual(7200d, history[0].FullPipelineMilliseconds!.Value, 0.001, "Failed attempt should retain full pipeline duration.");
    Assert.AreEqual(6350d, history[1].FullPipelineMilliseconds!.Value, 0.001, "Successful attempt should retain full pipeline duration.");
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

    [TestMethod]
    public async Task GetDataStoreMetricsAsync_ReportsDatabaseAndTelemetrySnapshots()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"hvo-diagnostics-tests-{Guid.NewGuid():N}");
        var dataPathProvider = new TestDataPathProvider(rootPath);

        var configurationPath = dataPathProvider.ResolvePath("configuration/sm-config.db");
        var telemetryPath = dataPathProvider.ResolvePath("telemetry/sm-telemetry.db");

        static SkyMonitorConfigurationContext CreateConfigurationContext(string path)
        {
            var options = new DbContextOptionsBuilder<SkyMonitorConfigurationContext>()
                .UseSqlite(new SqliteConnectionStringBuilder { DataSource = path }.ToString())
                .Options;

            var context = new SkyMonitorConfigurationContext(options);
            context.Database.EnsureCreated();
            return context;
        }

        static SkyMonitorTelemetryContext CreateTelemetryContext(string path)
        {
            var options = new DbContextOptionsBuilder<SkyMonitorTelemetryContext>()
                .UseSqlite(new SqliteConnectionStringBuilder { DataSource = path }.ToString())
                .Options;

            var context = new SkyMonitorTelemetryContext(options);
            context.Database.EnsureCreated();
            return context;
        }

        await using (var configurationContext = CreateConfigurationContext(configurationPath))
        {
            // Force schema creation and seed data.
            await configurationContext.Database.EnsureCreatedAsync();
        }

        await using (var telemetryContext = CreateTelemetryContext(telemetryPath))
        {
            telemetryContext.RemoteDispatchAttempts.Add(new RemoteDispatchAttemptEntity
            {
                AttemptedAtUtc = DateTimeOffset.UtcNow,
                AttemptedAtLocal = DateTimeOffset.UtcNow,
                Mode = "Test",
                Outcome = 1,
                LatencyMilliseconds = 12.3
            });

            telemetryContext.BackgroundStackerSamples.Add(new BackgroundStackerSampleEntity
            {
                CapturedAtUtc = DateTimeOffset.UtcNow,
                CapturedAtLocal = DateTimeOffset.UtcNow,
                QueueDepth = 1,
                QueueCapacity = 8,
                QueueFillPercentage = 12.5,
                QueuePressureLevel = 0,
                QueueMemoryMegabytes = 0.5
            });

            telemetryContext.SaveChanges();
        }

        var configurationFactory = new TestDbContextFactory<SkyMonitorConfigurationContext>(() => CreateConfigurationContext(configurationPath));
        var telemetryFactory = new TestDbContextFactory<SkyMonitorTelemetryContext>(() => CreateTelemetryContext(telemetryPath));

        var telemetryQueue = new TestTelemetryQueue { PendingCount = 3 };
        var telemetryMetrics = CreateTelemetryMetrics(telemetryQueue);
        telemetryMetrics.ReportIngestionLatency(TimeSpan.FromMilliseconds(87));

        var retentionStarted = DateTimeOffset.UtcNow.AddSeconds(-15);
        var retentionCompleted = retentionStarted.AddSeconds(6);
        var retentionSummary = new TelemetryRetentionSummary(
            RemoteDispatchPurged: 1,
            FrameExportsPurged: 2,
            BackgroundStackerPurged: 3,
            CapturePacingPurged: 4,
            ProcessingQueuePurged: 5,
            FilterMetricsPurged: 6,
            TelemetryEventsPurged: 7,
            TotalPurged: 28,
            VacuumAttempted: true,
            VacuumSucceeded: true);
        telemetryMetrics.ReportRetentionCompletion(retentionStarted, retentionCompleted, retentionSummary);

        var bootstrapStatus = new Mock<IDataStoreBootstrapStatus>();
        bootstrapStatus.Setup(status => status.GetSnapshot()).Returns(new DataStoreBootstrapSnapshot(
            DataStoreBootstrapState.Success(configurationPath, retentionStarted, retentionCompleted),
            DataStoreBootstrapState.Success(telemetryPath, retentionStarted, retentionCompleted)));

        var clock = CreateDefaultClockMock();
        clock.SetupGet(c => c.UtcNow).Returns(() => DateTimeOffset.UtcNow);
        clock.SetupGet(c => c.LocalNow).Returns(() => DateTimeOffset.UtcNow);
        clock.Setup(c => c.ToLocal(It.IsAny<DateTimeOffset>())).Returns<DateTimeOffset>(value => value);

        var frameStateStore = new Mock<IFrameStateStore>();
        var pipeline = new Mock<IFrameFilterPipeline>();

        var service = CreateService(
            frameStateStore.Object,
            pipeline.Object,
            clock,
            configurationFactory,
            telemetryFactory,
            dataPathProvider,
            telemetryQueue,
            telemetryMetrics,
            bootstrapStatus.Object);

        var result = await service.GetDataStoreMetricsAsync();

        Assert.IsTrue(result.IsSuccessful, "Data store metrics should resolve successfully.");

        var snapshot = result.Value;
        Assert.AreEqual(configurationPath, snapshot.ConfigurationStore.DatabasePath, "Configuration path should match resolved location.");
        Assert.AreEqual(telemetryPath, snapshot.TelemetryStore.DatabasePath, "Telemetry path should match resolved location.");
        Assert.IsTrue(snapshot.ConfigurationStore.Exists, "Configuration database should exist.");
        Assert.IsTrue(snapshot.TelemetryStore.Exists, "Telemetry database should exist.");
        Assert.IsTrue(snapshot.ConfigurationStore.FileBytes > 0, "Configuration database size should be reported.");
        Assert.IsTrue(snapshot.TelemetryStore.FileBytes > 0, "Telemetry database size should be reported.");

    Assert.IsTrue(snapshot.ConfigurationStore.Tables.Any(t => t.Table == "observatory_site" && t.RowCount > 0), "Seeded configuration tables should report row counts.");

    Assert.IsTrue(snapshot.TelemetryStore.Tables.Any(t => t.Table == "remote_dispatch_attempt" && t.RowCount == 1), "Telemetry table counts should reflect inserted rows.");
    Assert.IsTrue(snapshot.TelemetryStore.Tables.Any(t => t.Table == "background_stacker_sample" && t.RowCount == 1), "Telemetry table counts should reflect inserted rows.");
    Assert.IsTrue(snapshot.TelemetryStore.Tables.Any(t => t.Table == "frame_export_attempt" && t.RowCount == 0), "Telemetry table counts should include export attempts even when empty.");

    var telemetryIngestion = snapshot.TelemetryStore.TelemetryIngestion;
    Assert.IsNotNull(telemetryIngestion, "Telemetry ingestion metrics should be present.");
    Assert.AreEqual(telemetryQueue.PendingCount, telemetryIngestion!.QueueDepth, "Queue depth should match telemetry metrics snapshot.");
    Assert.AreEqual(87d, telemetryIngestion.LastIngestionLatencyMilliseconds, 0.001, "Ingestion latency should reflect latest metric.");
    var expectedTelemetryMegabytes = snapshot.TelemetryStore.FileMegabytes ?? 0d;
    Assert.AreEqual(expectedTelemetryMegabytes, telemetryIngestion.TelemetryDatabaseMegabytes, 0.001, "Telemetry database size gauge should align with file statistics.");
    var expectedTelemetryRows = snapshot.TelemetryStore.Tables.Sum(table => table.RowCount);
    Assert.AreEqual(expectedTelemetryRows, telemetryIngestion.TotalTelemetryRows, "Telemetry row count gauge should match aggregated table row totals.");

    var telemetryRetention = snapshot.TelemetryStore.TelemetryRetention;
    Assert.IsNotNull(telemetryRetention, "Telemetry retention snapshot should be present.");
    Assert.AreEqual(retentionSummary.FrameExportsPurged, telemetryRetention!.FrameExportsPurged, "Retention summary should propagate frame export purges.");
    Assert.AreEqual(retentionSummary.TotalPurged, telemetryRetention.TotalPurged, "Retention summary should propagate purge totals.");
    Assert.AreEqual(retentionCompleted, telemetryRetention.LastCompletedAtUtc, "Retention completion timestamp should propagate.");

        Assert.IsTrue(snapshot.TelemetryStore.Bootstrap.Ran && snapshot.TelemetryStore.Bootstrap.Succeeded, "Bootstrap status should indicate success.");
        Assert.IsTrue(snapshot.ConfigurationStore.Bootstrap.Ran && snapshot.ConfigurationStore.Bootstrap.Succeeded, "Configuration bootstrap status should indicate success.");
    }

    private static DiagnosticsService CreateService(
        IFrameStateStore frameStateStore,
        IFrameFilterPipeline pipeline,
        Mock<IObservatoryClock>? clockMock = null,
        IDbContextFactory<SkyMonitorConfigurationContext>? configurationContextFactory = null,
        IDbContextFactory<SkyMonitorTelemetryContext>? telemetryContextFactory = null,
        ISkyMonitorDataPathProvider? dataPathProvider = null,
        TestTelemetryQueue? telemetryQueue = null,
        SkyMonitorTelemetryMetrics? telemetryMetrics = null,
        IDataStoreBootstrapStatus? bootstrapStatus = null,
        ILogger<DiagnosticsService>? logger = null)
    {
        var clock = clockMock ?? CreateDefaultClockMock();
        configurationContextFactory ??= new TestDbContextFactory<SkyMonitorConfigurationContext>(CreateInMemoryConfigurationContext);
        telemetryContextFactory ??= new TestDbContextFactory<SkyMonitorTelemetryContext>(CreateInMemoryTelemetryContext);
        dataPathProvider ??= new TestDataPathProvider(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));

        telemetryQueue ??= new TestTelemetryQueue();
        telemetryMetrics ??= CreateTelemetryMetrics(telemetryQueue);

        bootstrapStatus ??= CreateBootstrapStatusMock().Object;
        logger ??= NullLogger<DiagnosticsService>.Instance;

        return new DiagnosticsService(
            frameStateStore,
            pipeline,
            configurationContextFactory,
            telemetryContextFactory,
            dataPathProvider,
            telemetryMetrics,
            bootstrapStatus,
            logger,
            clock.Object);
    }

    private static SkyMonitorConfigurationContext CreateInMemoryConfigurationContext()
    {
        var options = new DbContextOptionsBuilder<SkyMonitorConfigurationContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new SkyMonitorConfigurationContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    private static SkyMonitorTelemetryContext CreateInMemoryTelemetryContext()
    {
        var options = new DbContextOptionsBuilder<SkyMonitorTelemetryContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new SkyMonitorTelemetryContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    private static SkyMonitorTelemetryMetrics CreateTelemetryMetrics(TestTelemetryQueue queue)
    {
        return new SkyMonitorTelemetryMetrics(new TestMeterFactory(), queue, NullLogger<SkyMonitorTelemetryMetrics>.Instance);
    }

    private static Mock<IDataStoreBootstrapStatus> CreateBootstrapStatusMock()
    {
        var snapshot = new DataStoreBootstrapSnapshot(
            DataStoreBootstrapState.NotRun("configuration/sm-config.db"),
            DataStoreBootstrapState.NotRun("telemetry/sm-telemetry.db"));

        var mock = new Mock<IDataStoreBootstrapStatus>();
        mock.Setup(status => status.GetSnapshot()).Returns(snapshot);
        return mock;
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

    private sealed class TestDbContextFactory<TContext> : IDbContextFactory<TContext>
        where TContext : DbContext
    {
        private readonly Func<TContext> _factory;

        public TestDbContextFactory(Func<TContext> factory)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public TContext CreateDbContext() => _factory();

        public ValueTask<TContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => new(_factory());
    }

    private sealed class TestDataPathProvider : ISkyMonitorDataPathProvider
    {
        private readonly string _rootPath;

        public TestDataPathProvider(string rootPath)
        {
            _rootPath = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
            Directory.CreateDirectory(_rootPath);
        }

        public string RootPath => _rootPath;

        public string ResolvePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                throw new ArgumentException("Relative path cannot be null or empty.", nameof(relativePath));
            }

            var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(_rootPath, normalized));
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return fullPath;
        }
    }

    private sealed class TestTelemetryQueue : ISkyMonitorTelemetryIngestionQueue
    {
        public int PendingCount { get; set; }

        public bool TryWrite(TelemetryWorkItem workItem) => throw new NotSupportedException();

        public IAsyncEnumerable<TelemetryWorkItem> ReadAllAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        public Meter Create(string name) => new(name);

        public Meter Create(MeterOptions meterOptions) => new(meterOptions);

        public void Dispose()
        {
        }
    }
}
