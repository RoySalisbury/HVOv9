using System;
using System.Linq;
using HVO.SkyMonitorV5.RPi.Cameras;
using HVO.SkyMonitorV5.RPi.Cameras.Drivers;
using HVO.SkyMonitorV5.RPi.Cameras.Zwo;
using HVO.SkyMonitorV5.RPi.Tests.TestDrivers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HVO.SkyMonitorV5.RPi.Tests.Cameras.Drivers;

[TestClass]
public sealed class CameraDriverRegistryTests
{
    [TestMethod]
    public void GetDrivers_ReturnsDiscoveredDescriptors()
    {
        var registry = new CameraDriverRegistry();

        var descriptors = registry.GetDrivers();

        Assert.IsTrue(descriptors.Any(d => d.Id == CameraDriverIdentifiers.SimulationMockMono && d.ImplementationType == typeof(MockCameraAdapter)),
            "Mock monochrome adapter descriptor should be discovered.");
        Assert.IsTrue(descriptors.Any(d => d.Id == CameraDriverIdentifiers.SimulationMockColor && d.ImplementationType == typeof(MockColorCameraAdapter)),
            "Mock colour adapter descriptor should be discovered.");
        Assert.IsTrue(descriptors.Any(d => d.Id == CameraDriverIdentifiers.ZwoAsi && d.ImplementationType == typeof(ZwoCameraAdapter)),
            "ZWO adapter descriptor should be discovered.");
    }

    [TestMethod]
    public void TryGetDriver_ReturnsDescriptorById()
    {
        var registry = new CameraDriverRegistry();

        var found = registry.TryGetDriver(CameraDriverIdentifiers.ZwoAsi, out var descriptor);

        Assert.IsTrue(found, "Registry should resolve ZWO driver descriptor.");
        Assert.IsNotNull(descriptor);
        Assert.AreEqual(CameraDriverIdentifiers.ZwoAsi, descriptor.Id);
        Assert.AreEqual(typeof(ZwoCameraAdapter), descriptor.ImplementationType);
    }

    [TestMethod]
    public void TryGetDriver_DuplicateIds_ExposesSingleDescriptor()
    {
        var registry = new CameraDriverRegistry();

        var matches = registry
            .GetDrivers()
            .Where(descriptor => string.Equals(descriptor.Id, TestCameraDrivers.DuplicateDriverId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.AreEqual(1, matches.Length, "Registry should expose only one descriptor when duplicate ids are discovered.");

        var descriptor = matches[0];
        Assert.IsTrue(descriptor.ImplementationType.Name.Contains("DuplicateTestCameraAdapter", StringComparison.Ordinal));

        var resolved = registry.TryGetDriver(TestCameraDrivers.DuplicateDriverId, out var lookupDescriptor);
        Assert.IsTrue(resolved, "Duplicate id descriptor should be retrievable from the registry.");
        Assert.AreSame(descriptor, lookupDescriptor);
    }

    [TestMethod]
    public void TryGetDriver_ReturnsConfigurationTypeMetadata()
    {
        var registry = new CameraDriverRegistry();

        var found = registry.TryGetDriver(TestCameraDrivers.ConfigurableDriverId, out var descriptor);

        Assert.IsTrue(found, "Registry should resolve configurable test driver descriptor.");
        Assert.IsNotNull(descriptor.ConfigurationType, "Configuration type should be populated when declared on the attribute.");
        Assert.AreEqual(typeof(TestCameraDrivers.ConfigurableDriverSettings), descriptor.ConfigurationType);
    }
}
