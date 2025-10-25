using System;
using HVO.SkyMonitorV5.RPi.Cameras;
using HVO.SkyMonitorV5.RPi.Cameras.Drivers;
using HVO.SkyMonitorV5.RPi.Cameras.Zwo;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HVO.SkyMonitorV5.RPi.Tests.Cameras.Drivers;

[TestClass]
public sealed class CameraDriverAttributeTests
{
    [TestMethod]
    public void MockCameraAdapter_AttributeMetadata_IsConfigured()
    {
        var attribute = GetAttribute(typeof(MockCameraAdapter));

    Assert.AreEqual(CameraDriverIdentifiers.SimulationMockMono, attribute.Id);
        Assert.AreEqual("Mock All-Sky (Monochrome)", attribute.DisplayName);
        Assert.AreEqual("Synthetic monochrome fisheye adapter used for development and testing.", attribute.Description);
        Assert.AreEqual("1.0.0", attribute.Version);
        Assert.IsNull(attribute.ConfigurationType);

        CameraDriverAttribute.Validate(typeof(MockCameraAdapter), attribute);
    }

    [TestMethod]
    public void MockColorCameraAdapter_AttributeMetadata_IsConfigured()
    {
        var attribute = GetAttribute(typeof(MockColorCameraAdapter));

    Assert.AreEqual(CameraDriverIdentifiers.SimulationMockColor, attribute.Id);
        Assert.AreEqual("Mock All-Sky (Color)", attribute.DisplayName);
        Assert.AreEqual("Synthetic colour fisheye adapter emulating Bayer-pattern noise characteristics.", attribute.Description);
        Assert.AreEqual("1.0.0", attribute.Version);
        Assert.IsNull(attribute.ConfigurationType);

        CameraDriverAttribute.Validate(typeof(MockColorCameraAdapter), attribute);
    }

    [TestMethod]
    public void ZwoCameraAdapter_AttributeMetadata_IsConfigured()
    {
        var attribute = GetAttribute(typeof(ZwoCameraAdapter));

    Assert.AreEqual(CameraDriverIdentifiers.ZwoAsi, attribute.Id);
        Assert.AreEqual("ZWO ASI Camera", attribute.DisplayName);
        Assert.AreEqual("Native ASICamera2 adapter for ZWO ASI-series devices.", attribute.Description);
        Assert.AreEqual("1.0.0", attribute.Version);
        Assert.IsNull(attribute.ConfigurationType);

        CameraDriverAttribute.Validate(typeof(ZwoCameraAdapter), attribute);
    }

    private static CameraDriverAttribute GetAttribute(Type targetType)
    {
        var attribute = Attribute.GetCustomAttribute(targetType, typeof(CameraDriverAttribute)) as CameraDriverAttribute;
        Assert.IsNotNull(attribute, $"Camera driver attribute was not found on type '{targetType.FullName}'.");
        return attribute!;
    }
}
