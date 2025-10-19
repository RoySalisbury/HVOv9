using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.Data.Configurations;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Models.Rigs;
using HVO.SkyMonitorV5.RPi.Services;
using HVO.SkyMonitorV5.RPi.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HVO.SkyMonitorV5.RPi.Tests.Services;

[TestClass]
public sealed class OpticsConfigurationServiceTests
{
    [TestMethod]
    public async Task GetCatalogAsync_ReturnsSeededRig()
    {
        var databaseName = Guid.NewGuid().ToString();
        SeedDatabase(databaseName);
        var factory = new TestDbContextFactory<SkyMonitorConfigurationContext>(() => CreateContext(databaseName));
        var invalidator = new StubInvalidator();
        var service = new OpticsConfigurationService(factory, invalidator, NullLogger<OpticsConfigurationService>.Instance);

        var result = await service.GetCatalogAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.IsTrue(result.IsSuccessful, "Expected successful result.");
        var catalog = result.Value;
        Assert.AreEqual(1, catalog.Rigs.Count);
        Assert.AreEqual("Mock Fisheye", catalog.Rigs[0].DisplayName);
        Assert.AreEqual("MockFisheye", catalog.Rigs[0].Key);
    Assert.AreEqual("MockASI174MM", catalog.Rigs[0].CameraKey);
    Assert.AreEqual("Fujinon_FE185C086HA_1", catalog.Rigs[0].OpticsKey);
        Assert.AreEqual(0, invalidator.CallCount);
    }

    [TestMethod]
    public async Task CreateRigAsync_PersistsRigAndInvalidatesSnapshot()
    {
        var databaseName = Guid.NewGuid().ToString();
        SeedDatabase(databaseName);
        var factory = new TestDbContextFactory<SkyMonitorConfigurationContext>(() => CreateContext(databaseName));
        var invalidator = new StubInvalidator();
        var service = new OpticsConfigurationService(factory, invalidator, NullLogger<OpticsConfigurationService>.Instance);

        var request = new CreateRigRequest
        {
            Key = "NewRig",
            DisplayName = "New Rig",
            CameraKey = "MockASI174MM",
            OpticsKey = "Fujinon_FE185C086HA_1",
            BoresightAltitudeDegrees = 45.0,
            BoresightAzimuthDegrees = 135.0,
            IsActive = false
        };

        var result = await service.CreateRigAsync(request, CancellationToken.None).ConfigureAwait(false);

        Assert.IsTrue(result.IsSuccessful, result.Error?.Message);
        var catalog = result.Value;
        Assert.AreEqual(2, catalog.Rigs.Count);
        Assert.AreEqual(1, invalidator.CallCount);

        var created = catalog.Rigs.Single(rig => string.Equals(rig.Key, "NewRig", StringComparison.Ordinal));
        Assert.AreEqual("New Rig", created.DisplayName);
        Assert.IsFalse(created.IsActive);
    }

    [TestMethod]
    public async Task UpdateRigAsync_WithIncorrectRevision_ReturnsFailure()
    {
        var databaseName = Guid.NewGuid().ToString();
        SeedDatabase(databaseName);
        var factory = new TestDbContextFactory<SkyMonitorConfigurationContext>(() => CreateContext(databaseName));
        var invalidator = new StubInvalidator();
        var service = new OpticsConfigurationService(factory, invalidator, NullLogger<OpticsConfigurationService>.Instance);

        var request = new UpdateRigRequest
        {
            Revision = 99,
            DisplayName = "Changed",
            CameraKey = "MockASI174MM",
            OpticsKey = "Fujinon_FE185C086HA_1",
            BoresightAltitudeDegrees = 80,
            BoresightAzimuthDegrees = 10,
            IsActive = true
        };

        var result = await service.UpdateRigAsync(1, request, CancellationToken.None).ConfigureAwait(false);

        Assert.IsFalse(result.IsSuccessful, "Expected concurrency failure.");
        Assert.IsNotNull(result.Error);
        Assert.AreEqual(0, invalidator.CallCount);
    }

    [TestMethod]
    public async Task DeleteRigAsync_WhenReferenced_ReturnsFailure()
    {
        var databaseName = Guid.NewGuid().ToString();
        SeedDatabase(databaseName, includeAdapter: true);
        var factory = new TestDbContextFactory<SkyMonitorConfigurationContext>(() => CreateContext(databaseName));
        var invalidator = new StubInvalidator();
        var service = new OpticsConfigurationService(factory, invalidator, NullLogger<OpticsConfigurationService>.Instance);

        var result = await service.DeleteRigAsync(1, 1, CancellationToken.None).ConfigureAwait(false);

        Assert.IsFalse(result.IsSuccessful, "Expected delete failure when rig is referenced.");
        Assert.IsNotNull(result.Error);
        Assert.AreEqual(0, invalidator.CallCount);
    }

    private static SkyMonitorConfigurationContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<SkyMonitorConfigurationContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new SkyMonitorConfigurationContext(options);
    }

    private static void SeedDatabase(string databaseName, bool includeAdapter = false)
    {
        using var context = CreateContext(databaseName);
        context.Database.EnsureDeleted();
        context.Database.EnsureCreated();

        if (!includeAdapter)
        {
            var adapters = context.CameraAdapters.ToList();
            if (adapters.Count > 0)
            {
                context.CameraAdapters.RemoveRange(adapters);
                context.SaveChanges();
            }
        }
    }

    private sealed class StubInvalidator : IConfigurationSnapshotInvalidator
    {
        public int CallCount { get; private set; }

        public void InvalidateSnapshot()
        {
            CallCount++;
        }
    }
}
