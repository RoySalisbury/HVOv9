using System;
using System.Collections.Generic;
using FluentAssertions;
using HVO.Iot.Devices.Iot.Devices.Sequent;
using HVO.Iot.Devices.Implementation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HVO.Iot.Devices.Tests;

[TestClass]
public class WatchdogBatteryHatTests
{
    private static WatchdogBatteryHat CreateHat(FakeWatchdogRegisterClient client) => new(client, ownsClient: false);

    [TestMethod]
    public void GetWatchdogPeriodSeconds_ShouldReturnLittleEndianValue()
    {
        var client = new FakeWatchdogRegisterClient();
        client.SetRegisterBytes(0x03, 0x34, 0x12);
        var hat = CreateHat(client);

        var result = hat.GetWatchdogPeriodSeconds();

        result.IsSuccessful.Should().BeTrue();
        result.Value.Should().Be(0x1234);
    }

    [TestMethod]
    public void SetWatchdogPeriodSeconds_ShouldWriteLittleEndian()
    {
        var client = new FakeWatchdogRegisterClient();
        var hat = CreateHat(client);

        hat.SetWatchdogPeriodSeconds(0x1234).IsSuccessful.Should().BeTrue();

        client.Writes.Should().HaveCount(1);
        client.Writes[0].Register.Should().Be(0x01);
        client.Writes[0].Data.Should().Equal(0x34, 0x12);
        client.GetRegister(0x01).Should().Be(0x34);
        client.GetRegister(0x02).Should().Be(0x12);
    }

    [TestMethod]
    public void ReloadWatchdog_ShouldWriteReloadKey()
    {
        var client = new FakeWatchdogRegisterClient();
        var hat = CreateHat(client);

        hat.ReloadWatchdog().IsSuccessful.Should().BeTrue();

        client.Writes.Should().HaveCount(1);
        client.Writes[0].Register.Should().Be(0x00);
        client.Writes[0].Data.Should().Equal(0xCA);
        client.GetRegister(0x00).Should().Be(0xCA);
    }

    [TestMethod]
    public void GetRtc_ShouldReturnExpectedDateTime()
    {
        var client = new FakeWatchdogRegisterClient();
        client.SetRegisterBytes(31, 24, 6, 15, 8, 30, 45);
        var hat = CreateHat(client);

        var result = hat.GetRtc();

        result.IsSuccessful.Should().BeTrue();
        result.Value.Should().Be(new DateTime(2024, 6, 15, 8, 30, 45, DateTimeKind.Local));
    }

    [TestMethod]
    public void SetRtc_ShouldWriteAllDateComponents()
    {
        var client = new FakeWatchdogRegisterClient();
        var hat = CreateHat(client);
        var date = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Local);

        hat.SetRtc(date).IsSuccessful.Should().BeTrue();

        client.Writes.Should().HaveCount(1);
        client.Writes[0].Register.Should().Be(37);
        client.Writes[0].Data.Should().Equal(25, 1, 2, 3, 4, 5, 0xAA);
    }

    private sealed class FakeWatchdogRegisterClient : MemoryI2cRegisterClient
    {
        private readonly List<(byte Register, byte[] Data)> _writes = new();

        public FakeWatchdogRegisterClient()
            : base(1, 0x30)
        {
        }

        public List<(byte Register, byte[] Data)> Writes => _writes;

        public void SetRegisterBytes(byte startRegister, params byte[] values)
        {
            var span = RegisterSpan;
            for (var i = 0; i < values.Length; i++)
            {
                span[startRegister + i] = values[i];
            }
        }

        public byte GetRegister(int register) => RegisterSpan[register];

        protected override void OnWrite(byte register, ReadOnlySpan<byte> data)
        {
            _writes.Add((register, data.ToArray()));
            base.OnWrite(register, data);
        }
    }
}
