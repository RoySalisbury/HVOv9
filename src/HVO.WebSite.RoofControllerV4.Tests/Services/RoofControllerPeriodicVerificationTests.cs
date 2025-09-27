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
public class RoofControllerPeriodicVerificationTests
{
    private class FakeHat : FourRelayFourInputHat
    {
        public FakeHat() : base(new FakeI2cDevice()) { }
        public void SetInputs(bool in1,bool in2,bool in3,bool in4)
        {
            ((FakeI2cDevice)Device).SetDigitalInputs(in1,in2,in3,in4);
        }
        private class FakeI2cDevice : System.Device.I2c.I2cDevice
        {
            private readonly byte[] _regs = new byte[256];
            public override System.Device.I2c.I2cConnectionSettings ConnectionSettings { get; } = new(1,0x0e);
            public void SetDigitalInputs(bool in1,bool in2,bool in3,bool in4)
            {
                byte mask = 0; if (in1) mask|=0x01; if (in2) mask|=0x02; if (in3) mask|=0x04; if (in4) mask|=0x08; _regs[0x03]=mask;
            }
            public override void Read(Span<byte> buffer) => throw new NotSupportedException();
            public override void Write(ReadOnlySpan<byte> buffer)
            {
                if (buffer.Length < 2) return; var reg = buffer[0]; for(int i=1;i<buffer.Length;i++) _regs[reg+i-1] = buffer[i];
            }
            public override void WriteRead(ReadOnlySpan<byte> writeBuffer, Span<byte> readBuffer)
            {
                var reg=writeBuffer[0]; for(int i=0;i<readBuffer.Length;i++) readBuffer[i]=_regs[reg+i];
            }
            protected override void Dispose(bool disposing) { }
        }
    }

    private RoofControllerServiceV4 CreateService(FakeHat hat, TimeSpan verificationInterval)
    {
        var options = Options.Create(new RoofControllerOptionsV4
        {
            EnableDigitalInputPolling = false, // Simulate missed event scenario; rely on periodic reads
            DigitalInputPollInterval = TimeSpan.FromMilliseconds(5),
            SafetyWatchdogTimeout = TimeSpan.FromSeconds(5),
            UseNormallyClosedLimitSwitches = true,
            // Standard mapping: 1=Open(FWD) 2=Close(REV) 3=ClearFault 4=Stop
            OpenRelayId = 1,
            CloseRelayId = 2,
            ClearFaultRelayId = 3,
            StopRelayId = 4,
            EnablePeriodicVerificationWhileMoving = true,
            PeriodicVerificationInterval = verificationInterval
        });
        return new RoofControllerServiceV4(new NullLogger<RoofControllerServiceV4>(), options, hat);
    }

    [TestMethod]
    public async Task PeriodicVerification_ShouldDetectLimitWithoutEvent()
    {
        var hat = new FakeHat();
    hat.SetInputs(true,true,false,false); // Mid travel (NC: both HIGH)
        var svc = CreateService(hat, TimeSpan.FromMilliseconds(120));
        (await svc.Initialize(CancellationToken.None)).IsSuccessful.Should().BeTrue();

        var openResult = svc.Open();
        openResult.IsSuccessful.Should().BeTrue();
        svc.Status.Should().Be(RoofControllerStatus.Opening);

    // Now simulate we reached open limit (IN1 LOW) but no event fired (because polling disabled)
    hat.SetInputs(false,true,false,false);

        // Wait a bit longer than the verification interval for tick to occur
        await Task.Delay(350);

        svc.Status.Should().Be(RoofControllerStatus.Open, "periodic verification should force hardware refresh and detect open limit");
    }
}
