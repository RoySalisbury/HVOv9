using Microsoft.VisualStudio.TestTools.UnitTesting;
using HVO.Iot.Devices.Iot.Devices.Sequent;
using HVO.Iot.Devices.Tests.TestHelpers;
using FluentAssertions;

namespace HVO.Iot.Devices.Tests;

[TestClass]
public class FourRelayFourInputHatTests
{
    private FourRelayFourInputHat CreateHat(FakeI2cDevice dev) => new FourRelayFourInputHat(dev);

    [TestMethod]
    public void Relay_SetAndClear_ShouldReflectInMaskAndIndividualState()
    {
        var dev = new FakeI2cDevice();
        var hat = CreateHat(dev);

        hat.SetRelay(1, true).IsSuccessful.Should().BeTrue();
        hat.SetRelay(3, true).IsSuccessful.Should().BeTrue();
        hat.IsRelayOn(1).Value.Should().BeTrue();
        hat.IsRelayOn(2).Value.Should().BeFalse();
        hat.IsRelayOn(3).Value.Should().BeTrue();
        hat.IsRelayOn(4).Value.Should().BeFalse();
        hat.GetRelaysMask().Value.Should().Be((byte)0b0000_0101);

        hat.SetRelay(1, false).IsSuccessful.Should().BeTrue();
        hat.IsRelayOn(1).Value.Should().BeFalse();
        hat.GetRelaysMask().Value.Should().Be((byte)0b0000_0100);
    }

    [TestMethod]
    public void Relays_SetMask_ShouldUpdateAll()
    {
        var dev = new FakeI2cDevice();
        var hat = CreateHat(dev);
        hat.SetRelaysMask(0b0000_1010).IsSuccessful.Should().BeTrue();
        hat.IsRelayOn(2).Value.Should().BeTrue();
        hat.IsRelayOn(4).Value.Should().BeTrue();
        hat.IsRelayOn(1).Value.Should().BeFalse();
        hat.IsRelayOn(3).Value.Should().BeFalse();
    }

    [TestMethod]
    public void Relays_TrySetWithRetry_ShouldSucceed()
    {
        var dev = new FakeI2cDevice();
        var hat = CreateHat(dev);
        var result = hat.TrySetRelayWithRetry(2, true, attempts:2, delayMs:0);
        result.IsSuccessful.Should().BeTrue();
        hat.IsRelayOn(2).Value.Should().BeTrue();
    }

    [TestMethod]
    public void Leds_SetSingleAndBatch_ShouldReflectState()
    {
        var dev = new FakeI2cDevice();
        var hat = CreateHat(dev);

        hat.SetLed(1, true).IsSuccessful.Should().BeTrue();
        hat.IsLedOn(1).Value.Should().BeTrue();
        hat.IsLedOn(2).Value.Should().BeFalse();

        hat.SetAllLeds(true,false,true,false).IsSuccessful.Should().BeTrue();
        var tuple = hat.GetAllLeds().Value;
        tuple.led1.Should().BeTrue();
        tuple.led2.Should().BeFalse();
        tuple.led3.Should().BeTrue();
        tuple.led4.Should().BeFalse();
    }

    [TestMethod]
    public void LedModes_SetSingleAndBatch_ShouldReflect()
    {
        var dev = new FakeI2cDevice();
        var hat = CreateHat(dev);

        hat.SetLedMode(1, FourRelayFourInputHat.LedMode.Manual).IsSuccessful.Should().BeTrue();
        hat.GetLedMode(1).Value.Should().Be(FourRelayFourInputHat.LedMode.Manual);
        hat.GetLedMode(2).Value.Should().Be(FourRelayFourInputHat.LedMode.Auto);

        hat.SetAllLedModes(FourRelayFourInputHat.LedMode.Auto, FourRelayFourInputHat.LedMode.Manual, FourRelayFourInputHat.LedMode.Manual, FourRelayFourInputHat.LedMode.Auto).IsSuccessful.Should().BeTrue();
        var modes = hat.GetAllLedModes().Value;
        modes.led1.Should().Be(FourRelayFourInputHat.LedMode.Auto);
        modes.led2.Should().Be(FourRelayFourInputHat.LedMode.Manual);
        modes.led3.Should().Be(FourRelayFourInputHat.LedMode.Manual);
        modes.led4.Should().Be(FourRelayFourInputHat.LedMode.Auto);
    }

    [TestMethod]
    public void Leds_MaskOperations_ShouldBeConsistent()
    {
        var dev = new FakeI2cDevice();
        var hat = CreateHat(dev);
        hat.SetAllLeds(true,true,false,false).IsSuccessful.Should().BeTrue();
        hat.GetLedsMask().Value.Should().Be((byte)0b0000_0011);
        hat.SetLedsMask(0b0000_1100).IsSuccessful.Should().BeTrue();
        var tuple = hat.GetAllLeds().Value;
        tuple.led1.Should().BeFalse();
        tuple.led2.Should().BeFalse();
        tuple.led3.Should().BeTrue();
        tuple.led4.Should().BeTrue();
    }

    [TestMethod]
    public void LedModes_MaskOperations_ShouldBeConsistent()
    {
        var dev = new FakeI2cDevice();
        var hat = CreateHat(dev);
        hat.SetAllLedModes(FourRelayFourInputHat.LedMode.Manual, FourRelayFourInputHat.LedMode.Auto, FourRelayFourInputHat.LedMode.Manual, FourRelayFourInputHat.LedMode.Auto).IsSuccessful.Should().BeTrue();
        hat.GetLedModesMask().Value.Should().Be((byte)0b0000_0101);
        hat.SetLedModesMask(0b0000_1010).IsSuccessful.Should().BeTrue();
        var modes = hat.GetAllLedModes().Value;
        modes.led1.Should().Be(FourRelayFourInputHat.LedMode.Auto);
        modes.led2.Should().Be(FourRelayFourInputHat.LedMode.Manual);
        modes.led3.Should().Be(FourRelayFourInputHat.LedMode.Auto);
        modes.led4.Should().Be(FourRelayFourInputHat.LedMode.Manual);
    }

    [TestMethod]
    public void Counters_EnableDisableAndReset_ShouldWork()
    {
        var dev = new FakeI2cDevice();
        var hat = CreateHat(dev);
        hat.SetCounterEnabled(1, true).IsSuccessful.Should().BeTrue();
        hat.IsCounterEnabled(1).Value.Should().BeTrue();
        hat.SetCounterEnabled(1, false).IsSuccessful.Should().BeTrue();
        hat.IsCounterEnabled(1).Value.Should().BeFalse();
        // simulate a pulse count write
        dev.SetRegisterBytes(0x0D, new byte[]{1,0,0,0}); // pulse count start ch1
        hat.GetPulseCount(1).Value.Should().Be(1u);
        hat.ResetPulseCount(1).IsSuccessful.Should().BeTrue();
        hat.GetPulseCount(1).Value.Should().Be(0u);
    }

    [TestMethod]
    public void Encoders_EnableDisableAndReset_ShouldWork()
    {
        var dev = new FakeI2cDevice();
        var hat = CreateHat(dev);
        hat.SetEncoderEnabled(1, true).IsSuccessful.Should().BeTrue();
        hat.IsEncoderEnabled(1).Value.Should().BeTrue();
        hat.SetEncoderEnabled(1, false).IsSuccessful.Should().BeTrue();
        hat.IsEncoderEnabled(1).Value.Should().BeFalse();
        // simulate encoder count write
        dev.SetRegisterBytes(0x25, new byte[]{5,0,0,0});
        hat.GetEncoderCount(1).Value.Should().Be(5);
        hat.ResetEncoderCount(1).IsSuccessful.Should().BeTrue();
        hat.GetEncoderCount(1).Value.Should().Be(0);
    }

    [TestMethod]
    public void InputFrequencyAndPwm_ShouldReadConfiguredValues()
    {
        var dev = new FakeI2cDevice();
        var hat = CreateHat(dev);
        // frequency base 0x35, pwm fill base 0x2D
        dev.SetRegisterBytes(0x35, new byte[]{0x10, 0x27}); // 10000 little endian
        hat.GetInputFrequencyHz(1).Value.Should().Be(0x2710);
        dev.SetRegisterBytes(0x2D, new byte[]{0x88, 0x13}); // 5000 => 50.00%
        hat.GetPwmDutyCyclePercent(1).Value.Should().BeApproximately(50.0, 0.01);
    }

    [TestMethod]
    public void CurrentReadings_ShouldScaleValues()
    {
        var dev = new FakeI2cDevice();
        var hat = CreateHat(dev);
        // current register base 0x48, each 2 bytes signed scaled /1000
        dev.SetRegisterBytes(0x48, new byte[]{0xE8, 0x03}); // 1000 -> 1.000A
        hat.GetCurrentAmps(1).Value.Should().BeApproximately(1.0, 0.0001);
        dev.SetRegisterBytes(0x50, new byte[]{0xD0, 0x07}); // RMS base 0x50 for relay1? Actually 0x50 is RMS start.
        hat.GetCurrentRmsAmps(1).Value.Should().BeApproximately(2.0, 0.0001);
    }
}
