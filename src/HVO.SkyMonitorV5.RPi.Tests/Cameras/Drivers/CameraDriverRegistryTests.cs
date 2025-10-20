using System.Linq;
using HVO.SkyMonitorV5.RPi.Cameras;
using HVO.SkyMonitorV5.RPi.Cameras.Drivers;
using HVO.SkyMonitorV5.RPi.Cameras.Zwo;
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
}
