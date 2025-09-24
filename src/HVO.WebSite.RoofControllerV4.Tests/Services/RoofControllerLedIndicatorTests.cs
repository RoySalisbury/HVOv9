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
public class RoofControllerLedIndicatorTests
{
    private class FakeHat : FourRelayFourInputHat
    {
        public FakeHat() : base(new FakeI2cDeviceForRoof()) { }
        public byte LastLedsMask => ((FakeI2cDeviceForRoof)Device).LedMask;
        public void SimulateInputs(bool in1, bool in2, bool in3, bool in4)
        {
            // Use protected Sync through public API: Set internal registers by reflection is heavy; instead call GetAllDigitalInputs via backing device state
            var dev = (FakeI2cDeviceForRoof)Device;
            dev.SetDigitalInputs(in1, in2, in3, in4);
        }
        private class FakeI2cDeviceForRoof : System.Device.I2c.I2cDevice
        {
            private readonly byte[] _regs = new byte[256];
            public byte LedMask => _regs[0x05];
            public override System.Device.I2c.I2cConnectionSettings ConnectionSettings { get; } = new(1, 0x0e);
            public void SetDigitalInputs(bool in1,bool in2,bool in3,bool in4)
            {
                byte mask = 0;
                if (in1) mask |= 0x01; if (in2) mask |= 0x02; if (in3) mask |= 0x04; if (in4) mask |= 0x08;
                _regs[0x03] = mask; // _I2C_MEM_DIG_IN
            }
            public override void Read(Span<byte> buffer) => throw new System.NotSupportedException();
            public override void Write(ReadOnlySpan<byte> buffer)
            {
                byte reg = buffer[0];
                if (buffer.Length < 2) return;
                if (reg == 0x05) // LED value direct write
                {
                    _regs[0x05] = (byte)(buffer[1] & 0x0f);
                    return;
                }
                // generic
                for (int i = 1; i < buffer.Length; i++) _regs[reg + i - 1] = buffer[i];
            }
            public override void WriteRead(ReadOnlySpan<byte> writeBuffer, Span<byte> readBuffer)
            {
                byte reg = writeBuffer[0];
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
            SafetyWatchdogTimeout = System.TimeSpan.FromSeconds(10),
            StopRelayId = 1,
            OpenRelayId = 2,
            CloseRelayId = 3,
            ClearFault = 4
        });
        return new RoofControllerServiceV4(new NullLogger<RoofControllerServiceV4>(), options, hat);
    }

    [TestMethod]
    public async Task LedMask_ShouldReflectOpenClosedAndFaultStates()
    {
        var hat = new FakeHat();
        var svc = CreateService(hat);
        (await svc.Initialize(CancellationToken.None)).IsSuccessful.Should().BeTrue();

        // Initial: no limits, no fault -> all off
        hat.SimulateInputs(false,false,false,false);
        svc.ForceStatusRefresh();
        hat.LastLedsMask.Should().Be(0x00);

        // Open limit only -> LED1
        hat.SimulateInputs(true,false,false,false);
        svc.ForceStatusRefresh();
        hat.LastLedsMask.Should().Be(0x01);

        // Closed limit only -> LED2
        hat.SimulateInputs(false,true,false,false);
        svc.ForceStatusRefresh();
        hat.LastLedsMask.Should().Be(0x02);

        // Fault only -> LED3
        hat.SimulateInputs(false,false,true,false);
        svc.ForceStatusRefresh();
        hat.LastLedsMask.Should().Be(0x04);

        // Open + Fault -> LED1 + LED3
        hat.SimulateInputs(true,false,true,false);
        svc.ForceStatusRefresh();
        hat.LastLedsMask.Should().Be(0x05);

        // Closed + Fault -> LED2 + LED3
        hat.SimulateInputs(false,true,true,false);
        svc.ForceStatusRefresh();
        hat.LastLedsMask.Should().Be(0x06);

        // All three (open, closed, fault) -> LED1+2+3 (error condition)
    hat.SimulateInputs(true,true,true,false);
    svc.ForceStatusRefresh();
        hat.LastLedsMask.Should().Be(0x07);
    }

    // Use reflection to invoke protected UpdateRoofStatus to keep test non-invasive
    // Reflection helper removed; using internal ForceStatusRefresh instead via InternalsVisibleTo
}