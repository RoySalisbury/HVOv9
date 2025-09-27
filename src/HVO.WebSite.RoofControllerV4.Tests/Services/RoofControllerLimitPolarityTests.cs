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
public class RoofControllerLimitPolarityTests
{
    private class FakeHat : FourRelayFourInputHat
    {
        public FakeHat() : base(new FakeI2cDevice()) { }
        public void Simulate(bool in1, bool in2, bool in3, bool in4)
        {
            var dev = (FakeI2cDevice)Device;
            dev.SetDigital(in1, in2, in3, in4);
        }
        private class FakeI2cDevice : System.Device.I2c.I2cDevice
        {
            private readonly byte[] _regs = new byte[256];
            public override System.Device.I2c.I2cConnectionSettings ConnectionSettings { get; } = new(1, 0x0e);
            public void SetDigital(bool a, bool b, bool c, bool d)
            {
                byte mask = 0;
                if (a) mask |= 0x01; if (b) mask |= 0x02; if (c) mask |= 0x04; if (d) mask |= 0x08;
                _regs[0x03] = mask;
            }
            public override void Read(Span<byte> buffer) => throw new System.NotSupportedException();
            public override void Write(ReadOnlySpan<byte> buffer) { if (buffer.Length < 2) return; var reg = buffer[0]; for (int i=1;i<buffer.Length;i++) _regs[reg+i-1]= buffer[i]; }
            public override void WriteRead(ReadOnlySpan<byte> writeBuffer, Span<byte> readBuffer)
            { var reg = writeBuffer[0]; for (int i=0;i<readBuffer.Length;i++) readBuffer[i]=_regs[reg+i]; }
            protected override void Dispose(bool disposing) { }
        }
    }

    private static RoofControllerServiceV4 Create(FakeHat hat, bool useNc)
    {
        var options = Options.Create(new RoofControllerOptionsV4
        {
            EnableDigitalInputPolling = false,
            UseNormallyClosedLimitSwitches = useNc,
            SafetyWatchdogTimeout = System.TimeSpan.FromSeconds(30),
            OpenRelayId = 1,
            CloseRelayId = 2,
            ClearFaultRelayId = 3,
            StopRelayId = 4
        });
        return new RoofControllerServiceV4(new NullLogger<RoofControllerServiceV4>(), options, hat);
    }

    [TestMethod]
    public async Task NcPolarity_LowEqualsLimit()
    {
        var hat = new FakeHat();
        var svc = Create(hat, useNc: true);
        (await svc.Initialize(CancellationToken.None)).IsSuccessful.Should().BeTrue();

        // NC normal (travel) HIGH, limit reached LOW
        hat.Simulate(true, true, false, false); // both normal, no limits
        svc.ForceStatusRefresh();
        svc.Status.Should().Be(RoofControllerStatus.Stopped);

        hat.Simulate(false, true, false, false); // forward raw LOW -> Open limit
        svc.ForceStatusRefresh();
        svc.Status.Should().Be(RoofControllerStatus.Open);

        hat.Simulate(true, false, false, false); // reverse raw LOW -> Closed limit
        svc.ForceStatusRefresh();
        svc.Status.Should().Be(RoofControllerStatus.Closed);
    }

    [TestMethod]
    public async Task NoPolarity_HighEqualsLimit()
    {
        var hat = new FakeHat();
        var svc = Create(hat, useNc: false);
        (await svc.Initialize(CancellationToken.None)).IsSuccessful.Should().BeTrue();

        // NO normal (travel) LOW, limit reached HIGH
        hat.Simulate(false, false, false, false); // both normal
        svc.ForceStatusRefresh();
        svc.Status.Should().Be(RoofControllerStatus.Stopped);

        hat.Simulate(true, false, false, false); // forward HIGH -> Open limit
        svc.ForceStatusRefresh();
        svc.Status.Should().Be(RoofControllerStatus.Open);

        hat.Simulate(false, true, false, false); // reverse HIGH -> Closed limit
        svc.ForceStatusRefresh();
        svc.Status.Should().Be(RoofControllerStatus.Closed);
    }
}
