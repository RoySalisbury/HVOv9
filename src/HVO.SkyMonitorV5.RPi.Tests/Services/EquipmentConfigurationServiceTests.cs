using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using HVO;
using HVO.SkyMonitorV5.RPi.Cameras;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.Data.Configurations;
using HVO.SkyMonitorV5.Data.Configurations.Entities;
using HVO.SkyMonitorV5.RPi.Cameras.Drivers;
using HVO.SkyMonitorV5.RPi.Infrastructure;

using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Models.Cameras;
using HVO.SkyMonitorV5.RPi.Models.Rigs;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Services;
using HVO.SkyMonitorV5.RPi.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HVO.SkyMonitorV5.RPi.Tests.Services;

[TestClass]
public sealed class EquipmentConfigurationServiceTests
{
    [TestMethod]
    public async Task GetCatalogAsync_ReturnsSeededRig()
    {
        var databaseName = Guid.NewGuid().ToString();
        SeedDatabase(databaseName);

        var factory = new TestDbContextFactory<SkyMonitorConfigurationContext>(() => CreateContext(databaseName));
        var invalidator = new StubInvalidator();
        var runtime = new StubRigRuntimeUpdater();
        var registry = new StubDriverRegistry();

        var service = new EquipmentConfigurationService(factory, invalidator, runtime, registry, NullLogger<EquipmentConfigurationService>.Instance);

        var result = await service.GetCatalogAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.IsTrue(result.IsSuccessful, "Expected successful result.");
        var catalog = result.Value;
        Assert.AreEqual(1, catalog.Rigs.Count);
        Assert.AreEqual("Mock Fisheye", catalog.Rigs[0].DisplayName);
        Assert.AreEqual("MockFisheye", catalog.Rigs[0].Key);
        Assert.AreEqual("MockASI174MM", catalog.Rigs[0].CameraKey);
        Assert.AreEqual("Fujinon_FE185C086HA_1", catalog.Rigs[0].OpticsKey);
        Assert.AreEqual(0, invalidator.CallCount);
        Assert.AreEqual(0, runtime.CallCount);
    }

    [TestMethod]
    public async Task GetCameraDriversAsync_ReturnsOrderedDescriptors()
    {
        var databaseName = Guid.NewGuid().ToString();
        SeedDatabase(databaseName);

        var factory = new TestDbContextFactory<SkyMonitorConfigurationContext>(() => CreateContext(databaseName));
        var invalidator = new StubInvalidator();
        var runtime = new StubRigRuntimeUpdater();
        var registry = new StubDriverRegistry();
        registry.Register(CreateDescriptor("Driver.B", "Beta Driver", includeConfiguration: false));
        registry.Register(CreateDescriptor("Driver.A", "Alpha Driver", includeConfiguration: true));

        var service = new EquipmentConfigurationService(factory, invalidator, runtime, registry, NullLogger<EquipmentConfigurationService>.Instance);

        var result = await service.GetCameraDriversAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.IsTrue(result.IsSuccessful, result.Error?.Message);
        var payload = result.Value;
        Assert.AreEqual(2, payload.Drivers.Count);
        Assert.AreEqual("Alpha Driver", payload.Drivers[0].DisplayName, "Expected alphabetical ordering by display name.");
        Assert.IsTrue(payload.Drivers[0].SupportsConfiguration);
        Assert.IsFalse(string.IsNullOrWhiteSpace(payload.Drivers[0].ConfigurationType));
        Assert.AreEqual("Beta Driver", payload.Drivers[1].DisplayName);
        Assert.IsFalse(payload.Drivers[1].SupportsConfiguration);
    }

    [TestMethod]
    public async Task CreateRigAsync_PersistsRigAndInvalidatesSnapshot()
    {
        var databaseName = Guid.NewGuid().ToString();
        SeedDatabase(databaseName);

        var factory = new TestDbContextFactory<SkyMonitorConfigurationContext>(() => CreateContext(databaseName));
        var invalidator = new StubInvalidator();
        var runtime = new StubRigRuntimeUpdater();
        var registry = new StubDriverRegistry();

        var service = new EquipmentConfigurationService(factory, invalidator, runtime, registry, NullLogger<EquipmentConfigurationService>.Instance);

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
        Assert.AreEqual(1, runtime.CallCount);
        Assert.IsFalse(runtime.LastForceRestart ?? false);
    }

    [TestMethod]
    public async Task UpdateRigAsync_WithIncorrectRevision_ReturnsFailure()
    {
        var databaseName = Guid.NewGuid().ToString();
        SeedDatabase(databaseName);

        var factory = new TestDbContextFactory<SkyMonitorConfigurationContext>(() => CreateContext(databaseName));
        var invalidator = new StubInvalidator();
        var runtime = new StubRigRuntimeUpdater();
        var registry = new StubDriverRegistry();

        var service = new EquipmentConfigurationService(factory, invalidator, runtime, registry, NullLogger<EquipmentConfigurationService>.Instance);

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
        Assert.AreEqual(0, runtime.CallCount);
    }

    [TestMethod]
    public async Task UpdateRigAsync_DisablingLastActiveRig_ReturnsFailure()
    {
        var databaseName = Guid.NewGuid().ToString();
        SeedDatabase(databaseName);

        var factory = new TestDbContextFactory<SkyMonitorConfigurationContext>(() => CreateContext(databaseName));
        var invalidator = new StubInvalidator();
        var runtime = new StubRigRuntimeUpdater();
        var registry = new StubDriverRegistry();

        var service = new EquipmentConfigurationService(factory, invalidator, runtime, registry, NullLogger<EquipmentConfigurationService>.Instance);

        var request = new UpdateRigRequest
        {
            Revision = 1,
            DisplayName = "Mock Fisheye",
            CameraKey = "MockASI174MM",
            OpticsKey = "Fujinon_FE185C086HA_1",
            BoresightAltitudeDegrees = 90,
            BoresightAzimuthDegrees = 0,
            IsActive = false
        };

        var result = await service.UpdateRigAsync(1, request, CancellationToken.None).ConfigureAwait(false);

        Assert.IsFalse(result.IsSuccessful, "Expected failure when disabling the last active rig.");
        Assert.IsNotNull(result.Error);
        StringAssert.Contains(result.Error.Message, "At least one rig must remain active");
        Assert.AreEqual(0, invalidator.CallCount);
        Assert.AreEqual(0, runtime.CallCount);
    }

    [TestMethod]
    public async Task UpdateRigAsync_ActivatingNewRig_SetsActiveAndReloadsRuntime()
    {
        var databaseName = Guid.NewGuid().ToString();
        SeedDatabase(databaseName);

        await using (var context = CreateContext(databaseName))
        {
            context.RigCatalogEntries.Add(new RigCatalogEntryEntity
            {
                Id = 2,
                Key = "SimulatedFisheye",
                DisplayName = "Simulated Fisheye",
                CameraId = 1,
                LensId = 1,
                BoresightAltitudeDegrees = 90.0,
                BoresightAzimuthDegrees = 0.0,
                IsActive = false,
                Revision = 1
            });

            await context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }

        var factory = new TestDbContextFactory<SkyMonitorConfigurationContext>(() => CreateContext(databaseName));
        var invalidator = new StubInvalidator();
        var runtime = new StubRigRuntimeUpdater();
        var registry = new StubDriverRegistry();

        var service = new EquipmentConfigurationService(factory, invalidator, runtime, registry, NullLogger<EquipmentConfigurationService>.Instance);

        var request = new UpdateRigRequest
        {
            Revision = 1,
            DisplayName = "Simulated Fisheye",
            CameraKey = "MockASI174MM",
            OpticsKey = "Fujinon_FE185C086HA_1",
            BoresightAltitudeDegrees = 90,
            BoresightAzimuthDegrees = 0,
            IsActive = true
        };

        var result = await service.UpdateRigAsync(2, request, CancellationToken.None).ConfigureAwait(false);

        Assert.IsTrue(result.IsSuccessful, "Expected rig activation to succeed.");
        Assert.AreEqual(1, invalidator.CallCount);
        Assert.AreEqual(1, runtime.CallCount);
        Assert.IsTrue(runtime.LastForceRestart, "Rig runtime reload should be forced when activating a new rig.");

        await using (var verify = CreateContext(databaseName))
        {
            var rigs = await verify.RigCatalogEntries.OrderBy(r => r.Id).ToListAsync().ConfigureAwait(false);
            Assert.AreEqual(2, rigs.Count);
            Assert.IsFalse(rigs[0].IsActive, "Original rig should be deactivated.");
            Assert.IsTrue(rigs[1].IsActive, "New rig should be activated.");
        }
    }

    [TestMethod]
    public async Task DeleteRigAsync_WhenReferenced_ReturnsFailure()
    {
        var databaseName = Guid.NewGuid().ToString();
        SeedDatabase(databaseName);

        var factory = new TestDbContextFactory<SkyMonitorConfigurationContext>(() => CreateContext(databaseName));
        var invalidator = new StubInvalidator();
        var runtime = new StubRigRuntimeUpdater();
        var registry = new StubDriverRegistry();

        var service = new EquipmentConfigurationService(factory, invalidator, runtime, registry, NullLogger<EquipmentConfigurationService>.Instance);

        var result = await service.DeleteRigAsync(1, 1, CancellationToken.None).ConfigureAwait(false);

        Assert.IsFalse(result.IsSuccessful, "Expected delete failure when rig is referenced.");
        Assert.IsNotNull(result.Error);
        Assert.AreEqual(0, invalidator.CallCount);
        Assert.AreEqual(0, runtime.CallCount);
    }

    [TestMethod]
    public async Task DeleteRigAsync_LastActiveRig_ReturnsFailure()
    {
        var databaseName = Guid.NewGuid().ToString();
        SeedDatabase(databaseName);

        var factory = new TestDbContextFactory<SkyMonitorConfigurationContext>(() => CreateContext(databaseName));
        var invalidator = new StubInvalidator();
        var runtime = new StubRigRuntimeUpdater();
        var registry = new StubDriverRegistry();

        var service = new EquipmentConfigurationService(factory, invalidator, runtime, registry, NullLogger<EquipmentConfigurationService>.Instance);

        var result = await service.DeleteRigAsync(1, 1, CancellationToken.None).ConfigureAwait(false);

        Assert.IsFalse(result.IsSuccessful, "Expected failure when deleting the last active rig.");
        Assert.IsNotNull(result.Error);
        StringAssert.Contains(result.Error.Message, "Activate another rig");
        Assert.AreEqual(0, invalidator.CallCount);
        Assert.AreEqual(0, runtime.CallCount);
    }



    [TestMethod]
    public async Task CreateCameraAsync_WithInvalidDriverSettings_ReturnsFailure()
    {
        var databaseName = Guid.NewGuid().ToString();
        SeedDatabase(databaseName);

        var factory = new TestDbContextFactory<SkyMonitorConfigurationContext>(() => CreateContext(databaseName));
        var invalidator = new StubInvalidator();
        var runtime = new StubRigRuntimeUpdater();
        var registry = new StubDriverRegistry();
        registry.Register(CreateDescriptor("Driver.Typed", "Typed Driver", includeConfiguration: true));

        var service = new EquipmentConfigurationService(factory, invalidator, runtime, registry, NullLogger<EquipmentConfigurationService>.Instance);

        var request = BuildCameraCreateRequest("Driver.Typed", "{\"gain\":\"oops\"}");

        var result = await service.CreateCameraAsync(request, CancellationToken.None).ConfigureAwait(false);

        Assert.IsFalse(result.IsSuccessful, "Expected failure when driver settings cannot bind to typed configuration.");
        Assert.IsNotNull(result.Error);
        Assert.IsInstanceOfType(result.Error, typeof(InvalidOperationException));
        Assert.AreEqual(0, invalidator.CallCount, "Snapshot invalidator should not run on failure.");
    }

    [TestMethod]
    public async Task CreateCameraAsync_WithTypedDriverSettings_PersistsCanonicalJson()
    {
        var databaseName = Guid.NewGuid().ToString();
        SeedDatabase(databaseName);

        var factory = new TestDbContextFactory<SkyMonitorConfigurationContext>(() => CreateContext(databaseName));
        var invalidator = new StubInvalidator();
        var runtime = new StubRigRuntimeUpdater();
        var registry = new StubDriverRegistry();
        registry.Register(CreateDescriptor("Driver.Typed", "Typed Driver", includeConfiguration: true));

        var service = new EquipmentConfigurationService(factory, invalidator, runtime, registry, NullLogger<EquipmentConfigurationService>.Instance);

        const string payload = "{\"gain\":5,\"mode\":\"High\"}";
        var request = BuildCameraCreateRequest("Driver.Typed", payload);

        var result = await service.CreateCameraAsync(request, CancellationToken.None).ConfigureAwait(false);

        Assert.IsTrue(result.IsSuccessful, result.Error?.Message);
        Assert.AreEqual(1, invalidator.CallCount, "Snapshot invalidator should run on success.");

        var catalog = result.Value;
        var camera = catalog.Cameras.Single(item => item.Key == request.Key);
        Assert.AreEqual(CanonicalizeJson(payload), camera.DriverSettingsJson);
    }

    [TestMethod]
    public async Task UpdateCameraAsync_WithInvalidDriverSettings_ReturnsFailure()
    {
        var databaseName = Guid.NewGuid().ToString();
        SeedDatabase(databaseName);

        var factory = new TestDbContextFactory<SkyMonitorConfigurationContext>(() => CreateContext(databaseName));
        var invalidator = new StubInvalidator();
        var runtime = new StubRigRuntimeUpdater();
        var registry = new StubDriverRegistry();
        registry.Register(CreateDescriptor("Driver.Typed", "Typed Driver", includeConfiguration: true));

        var service = new EquipmentConfigurationService(factory, invalidator, runtime, registry, NullLogger<EquipmentConfigurationService>.Instance);

        var createRequest = BuildCameraCreateRequest("Driver.Typed", "{\"gain\":7}");
        var createResult = await service.CreateCameraAsync(createRequest, CancellationToken.None).ConfigureAwait(false);
        Assert.IsTrue(createResult.IsSuccessful, createResult.Error?.Message);

        var camera = createResult.Value.Cameras.Single(item => item.Key == createRequest.Key);
        Assert.AreEqual(1, invalidator.CallCount, "Create should invalidate snapshot once.");

        var updateRequest = BuildCameraUpdateRequest(createRequest, camera.Revision, "{\"gain\":\"oops\"}");

        var updateResult = await service.UpdateCameraAsync(camera.Id, updateRequest, CancellationToken.None).ConfigureAwait(false);

        Assert.IsFalse(updateResult.IsSuccessful, "Expected failure when updated settings fail typed validation.");
        Assert.IsNotNull(updateResult.Error);
        Assert.IsInstanceOfType(updateResult.Error, typeof(InvalidOperationException));
        Assert.AreEqual(1, invalidator.CallCount, "Invalid update should not invalidate snapshot.");
    }

    private static SkyMonitorConfigurationContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<SkyMonitorConfigurationContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new SkyMonitorConfigurationContext(options);
    }

    private static CreateCameraRequest BuildCameraCreateRequest(string driverId, string? driverSettingsJson)
        => new()
        {
            Key = "UnitTestCamera",
            DisplayName = "Unit Test Camera",
            Manufacturer = "TestCo",
            Model = "ModelX",
            DriverId = driverId,
            SensorWidthPixels = 1024,
            SensorHeightPixels = 768,
            PixelSizeMicrons = 3.2,
            ColorMode = "Mono",
            SensorTechnology = "CMOS",
            BodyType = "Test",
            Cooling = "None",
            SupportsGainControl = true,
            SupportsExposureControl = true,
            SupportsTemperatureTelemetry = false,
            SupportsSoftwareBinning = false,
            IsActive = true,
            AdditionalTags = new[] { "UnitTest" },
            DriverSettingsJson = driverSettingsJson
        };

    private static UpdateCameraRequest BuildCameraUpdateRequest(CreateCameraRequest template, long revision, string? driverSettingsJson)
        => new()
        {
            Revision = revision,
            Key = template.Key,
            DisplayName = template.DisplayName,
            Manufacturer = template.Manufacturer,
            Model = template.Model,
            DriverVersion = template.DriverVersion,
            AdapterName = template.AdapterName,
            DriverId = template.DriverId,
            SyntheticProfile = template.SyntheticProfile,
            IsSynthetic = template.IsSynthetic,
            SensorWidthPixels = template.SensorWidthPixels,
            SensorHeightPixels = template.SensorHeightPixels,
            PixelSizeMicrons = template.PixelSizeMicrons,
            SensorCxPixels = template.SensorCxPixels,
            SensorCyPixels = template.SensorCyPixels,
            ColorMode = template.ColorMode,
            SensorTechnology = template.SensorTechnology,
            BodyType = template.BodyType,
            Cooling = template.Cooling,
            SupportsGainControl = template.SupportsGainControl,
            SupportsExposureControl = template.SupportsExposureControl,
            SupportsTemperatureTelemetry = template.SupportsTemperatureTelemetry,
            SupportsSoftwareBinning = template.SupportsSoftwareBinning,
            IsActive = template.IsActive,
            AdditionalTags = template.AdditionalTags,
            DriverSettingsJson = driverSettingsJson
        };

    private static string CanonicalizeJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static void SeedDatabase(string databaseName)
    {
        using var context = CreateContext(databaseName);
        context.Database.EnsureDeleted();
        context.Database.EnsureCreated();
    }

    private sealed class StubInvalidator : IConfigurationSnapshotInvalidator
    {
        public int CallCount { get; private set; }

        public void InvalidateSnapshot()
        {
            CallCount++;
        }
    }

    private sealed class StubRigRuntimeUpdater : IRigRuntimeUpdater
    {
        public int CallCount { get; private set; }
        public bool? LastForceRestart { get; private set; }

        public Task ReloadActiveRigAsync(bool forceRestart, CancellationToken cancellationToken)
        {
            CallCount++;
            LastForceRestart = forceRestart;
            return Task.CompletedTask;
        }
    }

    private sealed class StubDriverRegistry : ICameraDriverRegistry
    {
        private readonly Dictionary<string, CameraDriverDescriptor> _drivers = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyCollection<CameraDriverDescriptor> GetDrivers()
            => _drivers.Values;

        public bool TryGetDriver(string id, out CameraDriverDescriptor descriptor)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                descriptor = null!;
                return false;
            }

            return _drivers.TryGetValue(id, out descriptor!);
        }

        public void Register(CameraDriverDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            _drivers[descriptor.Id] = descriptor;
        }
    }

    private static CameraDriverDescriptor CreateDescriptor(string id, string displayName, bool includeConfiguration)
    {
        var factory = ActivatorUtilities.CreateFactory(typeof(FakeCameraAdapter), new[] { typeof(RigSpec) });
        return (CameraDriverDescriptor)Activator.CreateInstance(
            typeof(CameraDriverDescriptor),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: new object?[]
            {
                id,
                displayName,
                $"Descriptor for {displayName}",
                "1.0.0",
                typeof(FakeCameraAdapter),
                includeConfiguration ? typeof(FakeCameraAdapterConfiguration) : null,
                factory
            },
            culture: null)!;
    }

    private sealed class FakeCameraAdapterConfiguration
    {
        public int Gain { get; set; }

        public string? Mode { get; set; }
    }

    private sealed class FakeCameraAdapter : ICameraAdapter
    {
        public FakeCameraAdapter(RigSpec rig)
        {
            Rig = rig;
        }

        public RigSpec Rig { get; }

        public Task<Result<bool>> InitializeAsync(CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Success(true));

        public Task<Result<bool>> ShutdownAsync(CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Success(true));

        public Task<Result<CapturedImage>> CaptureAsync(ExposureSettings exposure, CancellationToken cancellationToken)
            => Task.FromResult(Result<CapturedImage>.Failure(new NotSupportedException("Capture is not implemented for test doubles.")));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

