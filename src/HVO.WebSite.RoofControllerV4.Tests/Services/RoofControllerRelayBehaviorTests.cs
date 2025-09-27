using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using HVO.WebSite.RoofControllerV4.Logic;
using HVO.WebSite.RoofControllerV4.Models;
using HVO.Iot.Devices.Iot.Devices.Sequent;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HVO.WebSite.RoofControllerV4.Tests.Services;

[TestClass]
public class RoofControllerRelayBehaviorTests
{
    private class FakeHat : FourRelayFourInputHat
    {
        public FakeHat() : base(new FakeI2cDevice()) { }
        public void SetInputs(bool in1, bool in2, bool in3, bool in4)
        {
            ((FakeI2cDevice)Device).SetDigitalInputs(in1,in2,in3,in4);
        }
        public byte RelayMask => ((FakeI2cDevice)Device).GetRelayMask();
        private class FakeI2cDevice : System.Device.I2c.I2cDevice
        {
            private readonly byte[] _regs = new byte[256];
            public override System.Device.I2c.I2cConnectionSettings ConnectionSettings { get; } = new(1,0x0e);
            public void SetDigitalInputs(bool in1,bool in2,bool in3,bool in4)
            {
                byte mask = 0; if (in1) mask|=0x01; if (in2) mask|=0x02; if (in3) mask|=0x04; if (in4) mask|=0x08; _regs[0x03]=mask; // digital inputs
            }
            public byte GetRelayMask() => _regs[0x00]; // relay register
            public override void Read(Span<byte> buffer) => throw new NotSupportedException();
            public override void Write(ReadOnlySpan<byte> buffer)
            {
                if (buffer.Length < 2) return; 
                var reg = buffer[0];
                byte val = buffer[1];
                // Emulate relay command semantics of real HAT
                switch (reg)
                {
                    case 0x00: // direct relay mask write
                        _regs[0x00] = (byte)(val & 0x0f);
                        break;
                    case 0x01: // relay set (val = relay index 1..4)
                        if (val >=1 && val <=4)
                        {
                            _regs[0x00] = (byte)(_regs[0x00] | (1 << (val-1)));
                        }
                        break;
                    case 0x02: // relay clear
                        if (val >=1 && val <=4)
                        {
                            _regs[0x00] = (byte)(_regs[0x00] & ~(1 << (val-1)));
                        }
                        break;
                    default:
                        // Generic fallback: store raw value at addressed register
                        _regs[reg] = val;
                        break;
                }
                // Store any extra bytes generically
                for(int i=2;i<buffer.Length;i++) _regs[reg+i-1] = buffer[i];
            }
            public override void WriteRead(ReadOnlySpan<byte> writeBuffer, Span<byte> readBuffer)
            {
                var reg = writeBuffer[0]; for(int i=0;i<readBuffer.Length;i++) readBuffer[i]=_regs[reg+i];
            }
            protected override void Dispose(bool disposing) { }
        }
    }

    private class TestableRoofControllerService : RoofControllerServiceV4
    {
        public TestableRoofControllerService(IOptions<RoofControllerOptionsV4> opts, FourRelayFourInputHat hat)
            : base(new NullLogger<RoofControllerServiceV4>(), opts, hat) { }

        // Expose protected handlers for deterministic event simulation
        public void SimForwardLimitRaw(bool high) => OnForwardLimitSwitchChanged(high);
        public void SimReverseLimitRaw(bool high) => OnReverseLimitSwitchChanged(high);
        public void SimFaultRaw(bool high) => OnFaultNotificationChanged(high);
        public void SimAtSpeedRaw(bool high) => OnAtSpeedChanged(high);
    }

    private static TestableRoofControllerService Create(FakeHat hat, TimeSpan? watchdog = null)
    {
        var options = Options.Create(new RoofControllerOptionsV4
        {
            EnableDigitalInputPolling = false, // manual simulation via exposed handlers
            UseNormallyClosedLimitSwitches = true,
            DigitalInputPollInterval = TimeSpan.FromMilliseconds(5),
            SafetyWatchdogTimeout = watchdog ?? TimeSpan.FromSeconds(10),
            // Standard mapping: 1=Open 2=Close 3=ClearFault 4=Stop
            OpenRelayId = 1,
            CloseRelayId = 2,
            ClearFaultRelayId = 3,
            StopRelayId = 4
        });
        return new TestableRoofControllerService(options, hat);
    }

    [TestMethod]
    public async Task IdlePowerUp_ShouldReflectRelaySafeState_AndStatusMatchesLimits()
    {
        var hat = new FakeHat();
        // Scenario 1: Mid-travel (both HIGH) -> expect Stopped
        hat.SetInputs(true,true,false,false);
        var svc = Create(hat);
        (await svc.Initialize(CancellationToken.None)).IsSuccessful.Should().BeTrue();
        hat.RelayMask.Should().Be(0x00, "all relays de-energized, STOP asserted (fail-safe) at idle");
        svc.Status.Should().Be(RoofControllerStatus.Stopped);

        // Scenario 2: Open limit engaged (IN1 LOW, IN2 HIGH)
        var hat2 = new FakeHat();
        hat2.SetInputs(false,true,false,false);
        var svc2 = Create(hat2);
        (await svc2.Initialize(CancellationToken.None)).IsSuccessful.Should().BeTrue();
        svc2.Status.Should().Be(RoofControllerStatus.Open);
        hat2.RelayMask.Should().Be(0x00);

        // Scenario 3: Closed limit engaged (IN1 HIGH, IN2 LOW)
        var hat3 = new FakeHat();
        hat3.SetInputs(true,false,false,false);
        var svc3 = Create(hat3);
        (await svc3.Initialize(CancellationToken.None)).IsSuccessful.Should().BeTrue();
        svc3.Status.Should().Be(RoofControllerStatus.Closed);
        hat3.RelayMask.Should().Be(0x00);
    }

    [TestMethod]
    public async Task OpenSequence_ShouldEnergizeStopAndOpenRelays_ThenDropAtLimit()
    {
        var hat = new FakeHat();
        hat.SetInputs(true,true,false,false); // mid-travel
        var svc = Create(hat);
        (await svc.Initialize(CancellationToken.None)).IsSuccessful.Should().BeTrue();

        var openResult = svc.Open();
        openResult.IsSuccessful.Should().BeTrue();
        hat.RelayMask.Should().Be(0x03, "Stop + Open relays energized (bits 0 and 1)");
        svc.Status.Should().Be(RoofControllerStatus.Opening);

        // Simulate limit reached: raw LOW on IN1 for NC
    hat.SetInputs(false, true, false, false); // hardware now shows open limit engaged
    svc.SimForwardLimitRaw(false);
    // Force a status refresh to ensure cached evaluation consistent in test context
    svc.ForceStatusRefresh(true);
    hat.RelayMask.Should().Be(0x00, "All relays de-energized after limit stop");
    svc.Status.Should().Be(RoofControllerStatus.Open);
    }

    [TestMethod]
    public async Task CloseSequence_ShouldEnergizeStopAndCloseRelays_ThenDropAtLimit()
    {
        var hat = new FakeHat();
        hat.SetInputs(true,true,false,false); // mid-travel
        var svc = Create(hat);
        (await svc.Initialize(CancellationToken.None)).IsSuccessful.Should().BeTrue();

        var closeResult = svc.Close();
        closeResult.IsSuccessful.Should().BeTrue();
        hat.RelayMask.Should().Be(0x05, "Stop + Close relays energized (bits 0 and 2)");
        svc.Status.Should().Be(RoofControllerStatus.Closing);

        // Simulate reverse/closed limit reached: raw LOW on IN2
    hat.SetInputs(true, false, false, false); // hardware closed limit engaged
    svc.SimReverseLimitRaw(false);
    svc.ForceStatusRefresh(true);
    hat.RelayMask.Should().Be(0x00);
    svc.Status.Should().Be(RoofControllerStatus.Closed);
    }

    [TestMethod]
    public async Task ManualStopMidTravel_ShouldDeenergizeRelaysAndSetPartialStatuses()
    {
        var hat = new FakeHat();
        hat.SetInputs(true,true,false,false); // mid-travel
        var svc = Create(hat);
        (await svc.Initialize(CancellationToken.None)).IsSuccessful.Should().BeTrue();

        svc.Open().IsSuccessful.Should().BeTrue();
        hat.RelayMask.Should().Be(0x03);
        svc.Stop(RoofControllerStopReason.NormalStop).IsSuccessful.Should().BeTrue();
        hat.RelayMask.Should().Be(0x00);
        svc.Status.Should().Be(RoofControllerStatus.PartiallyOpen);

        // Now issue Close then manual stop
        svc.Close().IsSuccessful.Should().BeTrue();
        hat.RelayMask.Should().Be(0x05);
        svc.Stop(RoofControllerStopReason.NormalStop).IsSuccessful.Should().BeTrue();
        hat.RelayMask.Should().Be(0x00);
        svc.Status.Should().Be(RoofControllerStatus.PartiallyClose);
    }

    [TestMethod]
    public async Task FaultTrip_ShouldStopMovement_SetError_AndRefuseCommandsUntilCleared()
    {
        var hat = new FakeHat();
        hat.SetInputs(true,true,false,false); // mid-travel
        var svc = Create(hat);
        (await svc.Initialize(CancellationToken.None)).IsSuccessful.Should().BeTrue();
        svc.Open().IsSuccessful.Should().BeTrue();
        hat.RelayMask.Should().Be(0x03);

    // Simulate fault raw HIGH on IN3 (update hardware first)
    hat.SetInputs(true, true, true, false);
    svc.SimFaultRaw(true);
    hat.RelayMask.Should().Be(0x00);
    svc.Status.Should().Be(RoofControllerStatus.Error);

    // Further movement commands should fail while fault active
    svc.Open().IsSuccessful.Should().BeFalse();
    svc.Close().IsSuccessful.Should().BeFalse();

        // ClearFault pulses relay 4 (ClearFault id). Provide short pulse.
        var clearResult = await svc.ClearFault(50, CancellationToken.None);
        clearResult.IsSuccessful.Should().BeTrue();
    // After clearing fault, clear hardware fault bit then simulate raw LOW transition
    hat.SetInputs(true,true,false,false); // mid-travel, fault cleared
    svc.SimFaultRaw(false); // fault cleared raw LOW
    var reopen = svc.Open();
    reopen.IsSuccessful.Should().BeTrue();
    }

    [TestMethod]
    public async Task AtSpeedTransition_ShouldUpdateAtSpeedRunDuringMotion()
    {
        var hat = new FakeHat();
        hat.SetInputs(true,true,false,false); // mid-travel
        var svc = Create(hat);
        (await svc.Initialize(CancellationToken.None)).IsSuccessful.Should().BeTrue();

    svc.Open().IsSuccessful.Should().BeTrue();
    svc.AtSpeedRun.Should().BeFalse();

    // Simulate at-speed raw HIGH transition (set hardware input and invoke handler)
    hat.SetInputs(true,true,false,true); // set IN4 HIGH in hardware
    svc.SimAtSpeedRaw(true);
    // Force refresh to read hardware if needed
    svc.ForceStatusRefresh(true);
    svc.AtSpeedRun.Should().BeTrue();
        svc.Status.Should().Be(RoofControllerStatus.Opening, "status remains Opening while watchdog active");

    // Simulate reaching open limit
    hat.SetInputs(false,true,false,true); // open limit engaged, at-speed remains TRUE
    svc.SimForwardLimitRaw(false);
        svc.Status.Should().Be(RoofControllerStatus.Open);
    }
}
