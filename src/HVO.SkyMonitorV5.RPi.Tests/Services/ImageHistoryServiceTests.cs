using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.Data.Archive;
using HVO.SkyMonitorV5.Data.Archive.Entities;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Models.ImageHistory;
using HVO.SkyMonitorV5.RPi.Services;
using HVO.SkyMonitorV5.RPi.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace HVO.SkyMonitorV5.RPi.Tests.Services;

[TestClass]
public sealed class ImageHistoryServiceTests
{
    [TestMethod]
    public async Task GetThumbnailsAsync_ReturnsPagedResultsAndCursor()
    {
        var referenceTime = DateTimeOffset.Parse("2025-05-01T06:30:00Z", CultureInfo.InvariantCulture);
        var entities = new[]
        {
            CreateEntity(Guid.NewGuid(), referenceTime, "RigA", "CameraA", framesStacked: 12, integrationMilliseconds: 240, queueLatency: 110, processing: 420, fullPipeline: 530, includeRaw: true),
            CreateEntity(Guid.NewGuid(), referenceTime.AddMinutes(-45), "RigA", "CameraB", framesStacked: 8, integrationMilliseconds: 180, queueLatency: 90, processing: 360, fullPipeline: 430),
            CreateEntity(Guid.NewGuid(), referenceTime.AddHours(-2), "RigB", "CameraC", framesStacked: 5, integrationMilliseconds: 120, queueLatency: 70, processing: 280, fullPipeline: 330)
        };

        var localOffset = TimeSpan.FromHours(-7);
        var service = CreateService(entities, referenceTime, localOffset, out var databasePath);

        try
        {
            var initialRequest = new ImageHistoryThumbnailsRequest(null, null, null, null, 2, null);
            var firstPageResult = await service.GetThumbnailsAsync(initialRequest, CancellationToken.None);

            Assert.IsTrue(firstPageResult.IsSuccessful, firstPageResult.Error?.Message);

            var firstPage = firstPageResult.Value;
            Assert.AreEqual(2, firstPage.Items.Count);
            Assert.AreEqual(entities[0].FrameId, firstPage.Items[0].FrameId);
            Assert.AreEqual(entities[1].FrameId, firstPage.Items[1].FrameId);
            Assert.AreEqual(localOffset, firstPage.Items[0].CapturedAtLocal.Offset);
            Assert.IsNotNull(firstPage.NextCursor);

            var secondRequest = initialRequest with { Cursor = firstPage.NextCursor };
            var secondPageResult = await service.GetThumbnailsAsync(secondRequest, CancellationToken.None);

            Assert.IsTrue(secondPageResult.IsSuccessful, secondPageResult.Error?.Message);

            var secondPage = secondPageResult.Value;
            Assert.AreEqual(1, secondPage.Items.Count);
            Assert.AreEqual(entities[2].FrameId, secondPage.Items[0].FrameId);
            Assert.IsNull(secondPage.NextCursor);
        }
        finally
        {
            TryDelete(databasePath);
        }
    }

    [TestMethod]
    public async Task GetFrameAsync_ReturnsFailureWhenFrameMissing()
    {
        var service = CreateService(Array.Empty<FrameArchiveEntity>(), DateTimeOffset.UtcNow, TimeSpan.Zero, out var databasePath);

        try
        {
            var result = await service.GetFrameAsync(Guid.NewGuid(), CancellationToken.None);

            Assert.IsTrue(result.IsFailure);
            Assert.IsInstanceOfType(result.Error, typeof(InvalidOperationException));
        }
        finally
        {
            TryDelete(databasePath);
        }
    }

    [TestMethod]
    public async Task GetStatsAsync_ComputesAggregates()
    {
        var referenceTime = DateTimeOffset.Parse("2025-06-15T09:00:00Z", CultureInfo.InvariantCulture);
        var entities = new[]
        {
            CreateEntity(Guid.NewGuid(), referenceTime, "RigA", "CameraA", framesStacked: 10, integrationMilliseconds: 200, queueLatency: 120, processing: 400, fullPipeline: 520, includeRaw: true),
            CreateEntity(Guid.NewGuid(), referenceTime.AddHours(-1), "RigA", "CameraB", framesStacked: 9, integrationMilliseconds: 180, queueLatency: 150, processing: 450, fullPipeline: 600),
            CreateEntity(Guid.NewGuid(), referenceTime.AddHours(-4), "RigB", "CameraA", framesStacked: 6, integrationMilliseconds: 160, queueLatency: 90, processing: 380, fullPipeline: 470)
        };

        var localOffset = TimeSpan.FromHours(-7);
        var service = CreateService(entities, referenceTime, localOffset, out var databasePath);

        try
        {
            var request = new ImageHistoryStatsRequest(null, null, null, null);
            var result = await service.GetStatsAsync(request, CancellationToken.None);

            Assert.IsTrue(result.IsSuccessful, result.Error?.Message);

            var stats = result.Value;
            Assert.AreEqual(3L, stats.FrameCount);
            Assert.AreEqual(referenceTime, stats.NewestCapturedAtUtc);
            Assert.AreEqual(referenceTime.AddHours(-4), stats.OldestCapturedAtUtc);
            Assert.AreEqual(localOffset, stats.NewestCapturedAtLocal?.Offset);
            Assert.AreEqual(localOffset, stats.OldestCapturedAtLocal?.Offset);
            Assert.AreEqual("MST", stats.TimeZoneDisplayName);

            Assert.IsNotNull(stats.AverageQueueLatencyMilliseconds);
            Assert.AreEqual((120d + 150d + 90d) / 3d, stats.AverageQueueLatencyMilliseconds!.Value, 0.001, "Average queue latency should match input values.");

            Assert.IsNotNull(stats.AverageProcessingMilliseconds);
            Assert.AreEqual((400d + 450d + 380d) / 3d, stats.AverageProcessingMilliseconds!.Value, 0.001, "Average processing latency should match input values.");

            Assert.IsNotNull(stats.AverageFullPipelineMilliseconds);
            Assert.AreEqual((520d + 600d + 470d) / 3d, stats.AverageFullPipelineMilliseconds!.Value, 0.001, "Average pipeline latency should match input values.");

            var rigBreakdown = stats.RigBreakdown.ToDictionary(entry => entry.Name, entry => entry.Count);
            Assert.AreEqual(2L, rigBreakdown["RigA"]);
            Assert.AreEqual(1L, rigBreakdown["RigB"]);

            var cameraBreakdown = stats.CameraBreakdown.ToDictionary(entry => entry.Name, entry => entry.Count);
            Assert.AreEqual(2L, cameraBreakdown["CameraA"]);
            Assert.AreEqual(1L, cameraBreakdown["CameraB"]);
        }
        finally
        {
            TryDelete(databasePath);
        }
    }

    private static ImageHistoryService CreateService(IEnumerable<FrameArchiveEntity> seed, DateTimeOffset clockNow, TimeSpan localOffset, out string databasePath)
    {
        databasePath = Path.Combine(Path.GetTempPath(), FormattableString.Invariant($"image-history-tests-{Guid.NewGuid():N}.sqlite"));

        var options = new DbContextOptionsBuilder<ImageFrameArchiveContext>()
            .UseSqlite(FormattableString.Invariant($"Data Source={databasePath}"))
            .Options;

        using (var context = new ImageFrameArchiveContext(options))
        {
            context.Database.EnsureDeleted();
            context.Database.EnsureCreated();

            if (seed.Any())
            {
                context.FrameArchives.AddRange(seed);
                context.SaveChanges();
            }
        }

        var factory = new TestDbContextFactory<ImageFrameArchiveContext>(() => new ImageFrameArchiveContext(options));

        var clock = new Mock<IObservatoryClock>();
        clock.SetupGet(c => c.UtcNow).Returns(clockNow);
        clock.SetupGet(c => c.LocalNow).Returns(clockNow.ToOffset(localOffset));
        clock.SetupGet(c => c.TimeZoneDisplayName).Returns("MST");
        clock.Setup(c => c.ToLocal(It.IsAny<DateTimeOffset>())).Returns((DateTimeOffset value) => value.ToOffset(localOffset));

        return new ImageHistoryService(factory, clock.Object, NullLogger<ImageHistoryService>.Instance);
    }

    private static FrameArchiveEntity CreateEntity(
        Guid frameId,
        DateTimeOffset capturedAtUtc,
        string rig,
        string camera,
        int framesStacked,
        int integrationMilliseconds,
        double queueLatency,
        double processing,
        double fullPipeline,
        bool includeRaw = false)
    {
        var archivedAtUtc = capturedAtUtc.AddMinutes(1);

        return new FrameArchiveEntity
        {
            FrameId = frameId,
            CapturedAtUtc = capturedAtUtc,
            RigName = rig,
            CameraName = camera,
            FramesStacked = framesStacked,
            IntegrationMilliseconds = integrationMilliseconds,
            AppliedFilters = new[] { "Luminance" },
            QueueLatencyMilliseconds = queueLatency,
            ProcessingMilliseconds = processing,
            FullPipelineMilliseconds = fullPipeline,
            PayloadContentType = "image/jpeg",
            PayloadExtension = "jpg",
            ThumbnailFilePath = $"/tmp/{frameId:N}.jpg",
            MediaFilePath = $"/tmp/{frameId:N}.jpg",
            RawMediaFilePath = includeRaw ? $"/tmp/{frameId:N}.skimg" : null,
            ArchivedAtUtc = archivedAtUtc
        };
    }

    private static void TryDelete(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Ignore cleanup errors during test runs.
        }
    }
}
