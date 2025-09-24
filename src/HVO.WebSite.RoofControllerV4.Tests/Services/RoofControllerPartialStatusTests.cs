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
public class RoofControllerPartialStatusTests
{
    private class FakeHat : FourRelayFourInputHat
    {
        public FakeHat() : base(new FakeI2cDeviceForRoof()) { }
        public void SimulateInputs(bool in1, bool in2, bool in3, bool in4)
        {
            var dev = (FakeI2cDeviceForRoof)Device;
            dev.SetDigitalInputs(in1, in2, in3, in4);
        }
        private class FakeI2cDeviceForRoof : System.Device.I2c.I2cDevice
        {
            private readonly byte[] _regs = new byte[256];
            public override System.Device.I2c.I2cConnectionSettings ConnectionSettings { get; } = new(1, 0x0e);
            public void SetDigitalInputs(bool in1,bool in2,bool in3,bool in4)
            {
                byte mask = 0;
                if (in1) mask |= 0x01; if (in2) mask |= 0x02; if (in3) mask |= 0x04; if (in4) mask |= 0x08;
                _regs[0x03] = mask; // digital inputs register
            }
            public override void Read(Span<byte> buffer) => throw new System.NotSupportedException();
            public override void Write(ReadOnlySpan<byte> buffer)
            {
                // Allow LED writes but ignore for this test
                if (buffer.Length < 2) return;
                var reg = buffer[0];
                for (int i = 1; i < buffer.Length; i++) _regs[reg + i - 1] = buffer[i];
            }
            public override void WriteRead(ReadOnlySpan<byte> writeBuffer, Span<byte> readBuffer)
            {
                var reg = writeBuffer[0];
                for (int i = 0; i < readBuffer.Length; i++) readBuffer[i] = _regs[reg + i];
            }
            protected override void Dispose(bool disposing) { }
        }
    }

    private RoofControllerServiceV4 CreateService(FakeHat hat)
    {
        var options = Options.Create(new RoofControllerOptionsV4
        {
            EnableDigitalInputPolling = false,
            DigitalInputPollInterval = System.TimeSpan.FromMilliseconds(10),
            SafetyWatchdogTimeout = System.TimeSpan.FromSeconds(30), // ample time so watchdog considered active during motion
            StopRelayId = 1,
            OpenRelayId = 2,
            CloseRelayId = 3,
            ClearFault = 4
        });
        return new RoofControllerServiceV4(new NullLogger<RoofControllerServiceV4>(), options, hat);
    }

    [TestMethod]
    public async Task Open_ThenManualStop_ShouldTransitionToPartiallyOpen()
    {
        var hat = new FakeHat();
        var svc = CreateService(hat);
        (await svc.Initialize(CancellationToken.None)).IsSuccessful.Should().BeTrue();

        // Ensure we start with no limits active (mid-travel scenario)
        hat.SimulateInputs(false,false,false,false);
    svc.ForceStatusRefresh();
        svc.Status.Should().NotBe(RoofControllerStatus.Open); // not actually at open

        // Issue Open command -> should enter Opening
        var openResult = svc.Open();
        openResult.IsSuccessful.Should().BeTrue();
        svc.Status.Should().Be(RoofControllerStatus.Opening);

        // Immediately stop (simulate manual stop while mid-travel)
        var stopResult = svc.Stop(RoofControllerStopReason.NormalStop);
        stopResult.IsSuccessful.Should().BeTrue();
        svc.Status.Should().Be(RoofControllerStatus.PartiallyOpen);
        svc.LastStopReason.Should().Be(RoofControllerStopReason.NormalStop);
    }

    [TestMethod]
    public async Task Close_ThenManualStop_ShouldTransitionToPartiallyClose()
    {
        var hat = new FakeHat();
        var svc = CreateService(hat);
        (await svc.Initialize(CancellationToken.None)).IsSuccessful.Should().BeTrue();

        // Mid-travel scenario
        hat.SimulateInputs(false,false,false,false);
    svc.ForceStatusRefresh();

        var closeResult = svc.Close();
        closeResult.IsSuccessful.Should().BeTrue();
        svc.Status.Should().Be(RoofControllerStatus.Closing);

        var stopResult = svc.Stop(RoofControllerStopReason.NormalStop);
        stopResult.IsSuccessful.Should().BeTrue();
        svc.Status.Should().Be(RoofControllerStatus.PartiallyClose);
        svc.LastStopReason.Should().Be(RoofControllerStopReason.NormalStop);
    }

    // Reflection helper removed; using internal ForceStatusRefresh instead via InternalsVisibleTo
}
