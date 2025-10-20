using System;
using HVO.SkyMonitorV5.RPi.Cameras.Drivers;
using HVO.SkyMonitorV5.RPi.Cameras.Optics;
using HVO.SkyMonitorV5.RPi.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HVO.SkyMonitorV5.RPi.Tests.Cameras.Optics;

[TestClass]
public sealed class CameraSpecTests
{
    [TestMethod]
    public void DriverIdentifier_Defaults_ToMockMono_ForSyntheticMonochrome()
    {
        var spec = new CameraSpec(
            "MockMono",
            new SensorSpec(1936, 1216, 5.86),
            new CameraCapabilities { ColorMode = CameraColorMode.Monochrome },
            new CameraDescriptor("HVO", "Mock", string.Empty, "Mock", Array.Empty<string>()),
            CameraDriverId.Synthetic,
            IsSynthetic: true);

        Assert.AreEqual(CameraDriverIdentifiers.SimulationMockMono, spec.DriverIdentifier);
    }

    [TestMethod]
    public void DriverIdentifier_Defaults_ToMockColor_ForSyntheticColour()
    {
        var spec = new CameraSpec(
            "MockColor",
            new SensorSpec(1936, 1216, 5.86),
            new CameraCapabilities { ColorMode = CameraColorMode.Color },
            new CameraDescriptor("HVO", "Mock", string.Empty, "Mock", Array.Empty<string>()),
            CameraDriverId.Synthetic,
            IsSynthetic: true);

        Assert.AreEqual(CameraDriverIdentifiers.SimulationMockColor, spec.DriverIdentifier);
    }

    [TestMethod]
    public void DriverIdentifier_Defaults_ToZwo_ForZwoDriver()
    {
        var spec = new CameraSpec(
            "Zwo",
            new SensorSpec(1936, 1216, 5.86),
            new CameraCapabilities { ColorMode = CameraColorMode.Monochrome },
            new CameraDescriptor("ZWO", "ASI174", "1.0", "ZWO", Array.Empty<string>()),
            CameraDriverId.Zwo);

        Assert.AreEqual(CameraDriverIdentifiers.ZwoAsi, spec.DriverIdentifier);
    }

    [TestMethod]
    public void DriverIdentifier_UsesOverride_WhenProvided()
    {
        var spec = new CameraSpec(
            "Custom",
            new SensorSpec(1936, 1216, 5.86),
            new CameraCapabilities { ColorMode = CameraColorMode.Monochrome },
            new CameraDescriptor("Vendor", "Model", string.Empty, "Adapter", Array.Empty<string>()),
            CameraDriverId.Unknown) with
        {
            DriverIdentifierOverride = "Vendor.Custom"
        };

        Assert.AreEqual("Vendor.Custom", spec.DriverIdentifier);
    }
}
