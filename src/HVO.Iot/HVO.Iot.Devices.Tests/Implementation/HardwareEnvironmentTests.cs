using System;
using FluentAssertions;
using HVO.Iot.Devices.Implementation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HVO.Iot.Devices.Tests.Implementation;

[TestClass]
[DoNotParallelize]
public class HardwareEnvironmentTests
{
    [TestInitialize]
    public void Initialize()
    {
        ClearEnvironmentOverrides();
        HardwareEnvironment.ResetForTests();
    }

    [TestCleanup]
    public void Cleanup()
    {
        ClearEnvironmentOverrides();
        HardwareEnvironment.ResetForTests();
    }

    [TestMethod]
    public void IsRaspberryPi_ShouldRespectForcedOverrideTrue()
    {
        Environment.SetEnvironmentVariable("HVO_FORCE_RASPBERRY_PI", "true");

        var result = HardwareEnvironment.IsRaspberryPi();

        result.Should().BeTrue();
    }

    [TestMethod]
    public void IsRaspberryPi_ShouldRespectForcedOverrideFalse()
    {
        Environment.SetEnvironmentVariable("HVO_FORCE_RASPBERRY_PI", "false");

        var result = HardwareEnvironment.IsRaspberryPi();

        result.Should().BeFalse();
    }

    [TestMethod]
    public void IsRaspberryPi_ShouldHonorUseRealGpioEnvironmentVariable()
    {
        Environment.SetEnvironmentVariable("USE_REAL_GPIO", "true");

        var result = HardwareEnvironment.IsRaspberryPi();

        result.Should().BeTrue();
    }

    private static void ClearEnvironmentOverrides()
    {
        Environment.SetEnvironmentVariable("HVO_FORCE_RASPBERRY_PI", null);
        Environment.SetEnvironmentVariable("USE_REAL_GPIO", null);
    }
}
