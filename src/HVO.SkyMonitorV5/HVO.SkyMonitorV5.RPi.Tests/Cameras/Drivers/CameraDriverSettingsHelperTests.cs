using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.RPi.Cameras;
using HVO.SkyMonitorV5.RPi.Cameras.Drivers;
using HVO.SkyMonitorV5.RPi.Cameras.Optics;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HVO.SkyMonitorV5.RPi.Tests.Cameras.Drivers;

[TestClass]
public sealed class CameraDriverSettingsHelperTests
{
    [TestMethod]
    public void Resolve_WithTypedConfiguration_ReturnsTypedPayload()
    {
        var descriptor = CreateDescriptor(typeof(FakeConfiguration));
        const string json = "{ \"gain\": 42, \"mode\": \"High\" }";

        var result = CameraDriverSettingsHelper.Resolve(json, descriptor);

        Assert.IsTrue(result.IsSuccessful, result.Error?.Message);
        var payload = result.Value;
        Assert.AreEqual(descriptor.Id, payload.DriverId);
        Assert.AreSame(descriptor, payload.Descriptor);
        Assert.IsTrue(payload.HasTypedConfiguration);
        Assert.IsTrue(payload.HasRawJson);
        Assert.AreEqual(JsonValueKind.Object, payload.RawJson.ValueKind);
        var configuration = payload.Configuration as FakeConfiguration;
        Assert.IsNotNull(configuration);
        Assert.AreEqual(42, configuration.Gain);
        Assert.AreEqual("High", configuration.Mode);
    }

    [TestMethod]
    public void Resolve_WithDescriptorWithoutConfigurationType_PreservesRawJson()
    {
        var descriptor = CreateDescriptor(configurationType: null);
        const string json = "{ \"exposure\": 15 }";

        var result = CameraDriverSettingsHelper.Resolve(json, descriptor);

        Assert.IsTrue(result.IsSuccessful, result.Error?.Message);
        var payload = result.Value;
        Assert.AreEqual(descriptor.Id, payload.DriverId);
        Assert.AreSame(descriptor, payload.Descriptor);
        Assert.IsFalse(payload.HasTypedConfiguration);
        Assert.IsTrue(payload.HasRawJson);
        Assert.AreEqual(JsonValueKind.Object, payload.RawJson.ValueKind);
    }

    [TestMethod]
    public void Resolve_WithWhitespaceJson_ReturnsEmptyPayload()
    {
        var descriptor = CreateDescriptor(typeof(FakeConfiguration));

        var result = CameraDriverSettingsHelper.Resolve("   \n\t", descriptor);

        Assert.IsTrue(result.IsSuccessful, result.Error?.Message);
        var payload = result.Value;
        Assert.AreEqual(descriptor.Id, payload.DriverId);
        Assert.AreSame(descriptor, payload.Descriptor);
        Assert.IsFalse(payload.HasTypedConfiguration);
        Assert.IsFalse(payload.HasRawJson);
    }

    [TestMethod]
    public void Resolve_WithInvalidJson_ReturnsFailure()
    {
        var descriptor = CreateDescriptor(typeof(FakeConfiguration));
        const string json = "{ invalid";

        var result = CameraDriverSettingsHelper.Resolve(json, descriptor);

        Assert.IsFalse(result.IsSuccessful);
        Assert.IsInstanceOfType(result.Error, typeof(InvalidOperationException));
    }

    [TestMethod]
    public void Resolve_WithRegistryAndDescriptor_BindsTypedConfiguration()
    {
        var descriptor = CreateDescriptor(typeof(FakeConfiguration));
        var registry = new StubRegistry(descriptor);
        var camera = CreateCameraSpec(descriptor.Id, "{ \"gain\": 7 }");

        var result = CameraDriverSettingsHelper.Resolve(camera, registry);

        Assert.IsTrue(result.IsSuccessful, result.Error?.Message);
        var payload = result.Value;
        Assert.AreEqual(descriptor.Id, payload.DriverId);
        Assert.AreSame(descriptor, payload.Descriptor);
        Assert.IsTrue(payload.HasTypedConfiguration);
        var configuration = payload.Configuration as FakeConfiguration;
        Assert.IsNotNull(configuration);
        Assert.AreEqual(7, configuration.Gain);
    }

    [TestMethod]
    public void Resolve_WithUnknownDriver_UsesRawFallback()
    {
        var registry = new StubRegistry();
        const string driverId = "Missing.Driver";
        var camera = CreateCameraSpec(driverId, "{ \"foo\": \"bar\" }");

        var result = CameraDriverSettingsHelper.Resolve(camera, registry);

        Assert.IsTrue(result.IsSuccessful, result.Error?.Message);
        var payload = result.Value;
        Assert.AreEqual(driverId, payload.DriverId);
        Assert.IsNull(payload.Descriptor);
        Assert.IsFalse(payload.HasTypedConfiguration);
        Assert.IsTrue(payload.HasRawJson);
        Assert.AreEqual(JsonValueKind.Object, payload.RawJson.ValueKind);
    }

    private static CameraSpec CreateCameraSpec(string driverId, string? driverSettings)
    {
        var sensor = new SensorSpec(4, 4, 3.2);
        var descriptor = new CameraDescriptor("TestCo", "TestCam", "1.0.0", "Adapter", Array.Empty<string>());
        var spec = new CameraSpec(
            Name: "Test Camera",
            Sensor: sensor,
            Capabilities: CameraCapabilities.Empty,
            Descriptor: descriptor,
            DriverId: CameraDriverId.Unknown,
            IsSynthetic: false,
            SyntheticProfile: null,
            DriverSettingsJson: driverSettings);

        return spec with { DriverIdentifierOverride = driverId };
    }

    private static CameraDriverDescriptor CreateDescriptor(Type? configurationType)
    {
        var factory = ActivatorUtilities.CreateFactory(typeof(FakeAdapter), new[] { typeof(RigSpec) });
        return (CameraDriverDescriptor)Activator.CreateInstance(
            typeof(CameraDriverDescriptor),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: new object?[]
            {
                "Test.Driver",
                "Test Driver",
                "Test driver for unit tests",
                "1.0.0",
                typeof(FakeAdapter),
                configurationType,
                factory
            },
            culture: null)!;
    }

    private sealed class StubRegistry : ICameraDriverRegistry
    {
        private readonly Dictionary<string, CameraDriverDescriptor> _map;

        public StubRegistry(params CameraDriverDescriptor[] descriptors)
        {
            _map = new Dictionary<string, CameraDriverDescriptor>(StringComparer.OrdinalIgnoreCase);
            if (descriptors is null)
            {
                return;
            }

            foreach (var descriptor in descriptors)
            {
                _map[descriptor.Id] = descriptor;
            }
        }

        public IReadOnlyCollection<CameraDriverDescriptor> GetDrivers() => _map.Values;

        public bool TryGetDriver(string id, out CameraDriverDescriptor descriptor)
        {
            if (_map.TryGetValue(id, out var value))
            {
                descriptor = value;
                return true;
            }

            descriptor = null!;
            return false;
        }
    }

    private sealed class FakeAdapter : ICameraAdapter
    {
        public FakeAdapter(RigSpec rig)
        {
            Rig = rig;
        }

        public RigSpec Rig { get; }

        public Task<Result<CapturedImage>> CaptureAsync(ExposureSettings exposure, CancellationToken cancellationToken)
            => Task.FromResult(Result<CapturedImage>.Failure(new NotSupportedException()));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task<Result<bool>> InitializeAsync(CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Success(true));

        public Task<Result<bool>> ShutdownAsync(CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Success(true));
    }

    private sealed class FakeConfiguration
    {
        public int Gain { get; set; }

        public string? Mode { get; set; }
    }
}
