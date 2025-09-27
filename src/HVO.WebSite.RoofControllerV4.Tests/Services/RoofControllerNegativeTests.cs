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

/// <summary>
/// Negative / defensive tests focused on invalid or fault scenarios and idempotency.
/// These tests complement the positive-path relay behavior suite.
/// </summary>
[TestClass]
public class RoofControllerNegativeTests
{
    // Local copy of FakeHat used in relay behavior tests (kept separate for clarity & isolation)
    private class FakeHat : FourRelayFourInputHat
    {
        public FakeHat() : base(new FakeI2cDevice()) { }
        public void SetInputs(bool in1, bool in2, bool in3, bool in4)
        {
            ((FakeI2cDevice)Device).SetDigitalInputs(in1, in2, in3, in4);
        }
        public byte RelayMask => ((FakeI2cDevice)Device).GetRelayMask();

        private class FakeI2cDevice : System.Device.I2c.I2cDevice
        {
            private readonly byte[] _regs = new byte[256];
            public override System.Device.I2c.I2cConnectionSettings ConnectionSettings { get; } = new(1, 0x0e);
            public void SetDigitalInputs(bool in1, bool in2, bool in3, bool in4)
            {
                byte mask = 0;
                if (in1) mask |= 0x01;
                if (in2) mask |= 0x02;
                if (in3) mask |= 0x04;
                if (in4) mask |= 0x08;
                _regs[0x03] = mask; // inputs register (arbitrary)
            }
            public byte GetRelayMask() => _regs[0x00];
            public override void Read(Span<byte> buffer) => throw new NotSupportedException();
            public override void Write(ReadOnlySpan<byte> buffer)
            {
                if (buffer.Length < 2) return;
                var reg = buffer[0];
                var val = buffer[1];
                switch (reg)
                {
                    case 0x00: // direct write
                        _regs[0x00] = (byte)(val & 0x0F);
                        break;
                    case 0x01: // set relay (1..4)
                        if (val is >= 1 and <= 4)
                            _regs[0x00] |= (byte)(1 << (val - 1));
                        break;
                    case 0x02: // clear relay (1..4)
                        if (val is >= 1 and <= 4)
                            _regs[0x00] &= (byte)~(1 << (val - 1));
                        break;
                    default:
                        _regs[reg] = val;
                        break;
                }
            }
            public override void WriteRead(ReadOnlySpan<byte> writeBuffer, Span<byte> readBuffer)
            {
                var start = writeBuffer[0];
                for (int i = 0; i < readBuffer.Length; i++)
                    readBuffer[i] = _regs[start + i];
            }
            protected override void Dispose(bool disposing) { }
        }
    }

    private class TestableRoofControllerService : RoofControllerServiceV4
    {
        public TestableRoofControllerService(IOptions<RoofControllerOptionsV4> opts, FourRelayFourInputHat hat)
            : base(new NullLogger<RoofControllerServiceV4>(), opts, hat) { }

        public void SimFaultRaw(bool high) => OnFaultNotificationChanged(high);
        public void SimForwardLimitRaw(bool high) => OnForwardLimitSwitchChanged(high);
        public void SimReverseLimitRaw(bool high) => OnReverseLimitSwitchChanged(high);

        // Expose protected relay setter for guard validation
        public void ForceRelayStates(bool stop, bool open, bool close) => SetRelayStatesAtomically(stop, open, close);
    }

    private static TestableRoofControllerService Create(FakeHat hat)
    {
        var options = Options.Create(new RoofControllerOptionsV4
        {
            EnableDigitalInputPolling = false,
            UseNormallyClosedLimitSwitches = true,
            DigitalInputPollInterval = TimeSpan.FromMilliseconds(5),
            SafetyWatchdogTimeout = TimeSpan.FromSeconds(5),
            // Standard mapping: 1=Open 2=Close 3=ClearFault 4=Stop
            OpenRelayId = 1,
            CloseRelayId = 2,
            ClearFaultRelayId = 3,
            StopRelayId = 4
        });
        return new TestableRoofControllerService(options, hat);
    }

    [TestMethod]
    public async Task Open_WithBothLimitsActive_ShouldFailAndSetErrorStatus()
    {
        // Both limits active (NC -> raw LOW means triggered) => in1 LOW, in2 LOW
        var hat = new FakeHat();
        hat.SetInputs(false, false, false, false);
        var svc = Create(hat);
        (await svc.Initialize(CancellationToken.None)).IsSuccessful.Should().BeTrue();

        var result = svc.Open();
        result.IsSuccessful.Should().BeFalse();
        svc.Status.Should().Be(RoofControllerStatus.Error);
        hat.RelayMask.Should().Be(0x00, "No relays energized when command refused");
    }

    [TestMethod]
    public async Task Close_WithBothLimitsActive_ShouldFailAndSetErrorStatus()
    {
        var hat = new FakeHat();
        hat.SetInputs(false, false, false, false);
        var svc = Create(hat);
        (await svc.Initialize(CancellationToken.None)).IsSuccessful.Should().BeTrue();

        var result = svc.Close();
        result.IsSuccessful.Should().BeFalse();
        svc.Status.Should().Be(RoofControllerStatus.Error);
        hat.RelayMask.Should().Be(0x00);
    }

    [TestMethod]
    public async Task MovementAttemptWhileFaultActive_ShouldBeRefused()
    {
        // Mid travel (both HIGH) but fault HIGH
        var hat = new FakeHat();
        hat.SetInputs(true, true, true, false);
        var svc = Create(hat);
        (await svc.Initialize(CancellationToken.None)).IsSuccessful.Should().BeTrue();

        // Open refused
        var open = svc.Open();
        open.IsSuccessful.Should().BeFalse();
        svc.Status.Should().Be(RoofControllerStatus.Stopped);
        hat.RelayMask.Should().Be(0x00);

        // Close also refused
        var close = svc.Close();
        close.IsSuccessful.Should().BeFalse();
        svc.Status.Should().Be(RoofControllerStatus.Stopped);
    }

    [TestMethod]
    public async Task RelayGuard_ShouldPreventSimultaneousOpenAndClose()
    {
        var hat = new FakeHat();
        hat.SetInputs(true, true, false, false); // mid travel
        var svc = Create(hat);
        (await svc.Initialize(CancellationToken.None)).IsSuccessful.Should().BeTrue();

        // Force an invalid request (open & close true). Guard should neutralize open/close and leave STOP energized or not based on logic.
        svc.ForceRelayStates(stop: true, open: true, close: true);

        // After guard: open & close must NOT both be present
        var mask = hat.RelayMask;
        (mask & 0x06).Should().NotBe(0x06, "Open and Close relays must never be energized simultaneously");
    }

    [TestMethod]
    public async Task Stop_ShouldBeIdempotent_WhenAlreadyStopped()
    {
        var hat = new FakeHat();
        hat.SetInputs(true, true, false, false); // mid travel -> initializes as Stopped
        var svc = Create(hat);
        (await svc.Initialize(CancellationToken.None)).IsSuccessful.Should().BeTrue();

        var first = svc.Stop(RoofControllerStopReason.NormalStop);
        first.IsSuccessful.Should().BeTrue();
        var mask1 = hat.RelayMask;
        mask1.Should().Be(0x00);

        var second = svc.Stop(RoofControllerStopReason.NormalStop);
        second.IsSuccessful.Should().BeTrue();
        hat.RelayMask.Should().Be(mask1);
        svc.Status.Should().Be(RoofControllerStatus.Stopped);
    }

    [TestMethod]
    public async Task BothLimitsError_ShouldContinueToRefuseSubsequentCommands()
    {
        var hat = new FakeHat();
        hat.SetInputs(false, false, false, false); // both limits
        var svc = Create(hat);
        (await svc.Initialize(CancellationToken.None)).IsSuccessful.Should().BeTrue();

        svc.Open().IsSuccessful.Should().BeFalse();
        svc.Status.Should().Be(RoofControllerStatus.Error);
        svc.Close().IsSuccessful.Should().BeFalse();
        svc.Status.Should().Be(RoofControllerStatus.Error);
        hat.RelayMask.Should().Be(0x00);
    }
}
