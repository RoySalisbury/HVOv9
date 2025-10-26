using System;
using HVO.Iot.Devices.Implementation;

namespace HVO.Iot.Devices.Tests.TestHelpers;

/// <summary>
/// In-memory fake I2C register client that emulates the subset of behavior required by FourRelayFourInputHat.
/// Supports register based Write/Read with special handling for relay/LED set & clear registers.
/// </summary>
public class FakeI2cDevice : MemoryI2cRegisterClient
{
    public FakeI2cDevice(int busId = 1, int address = 0x0e)
        : base(busId, address)
    {
    }

    public void SetRegister(byte reg, byte value) => RegisterSpan[reg] = value;

    public void SetRegisterBytes(byte startReg, ReadOnlySpan<byte> data) => data.CopyTo(RegisterSpan.Slice(startReg, data.Length));

    public byte GetRegister(byte reg) => RegisterSpan[reg];

    protected override void OnWrite(byte register, ReadOnlySpan<byte> data)
    {
        switch (register)
        {
            case 0x01: // _I2C_MEM_RELAY_SET
                if (data.Length >= 1)
                {
                    int relayIndex = data[0];
                    if (relayIndex is >= 1 and <= 4)
                    {
                        RegisterSpan[0x00] = (byte)(RegisterSpan[0x00] | (1 << (relayIndex - 1)));
                    }
                }
                return;
            case 0x02: // _I2C_MEM_RELAY_CLR
                if (data.Length >= 1)
                {
                    int relayIndex = data[0];
                    if (relayIndex is >= 1 and <= 4)
                    {
                        RegisterSpan[0x00] = (byte)(RegisterSpan[0x00] & ~(1 << (relayIndex - 1)));
                    }
                }
                return;
            case 0x06: // _I2C_MEM_LED_SET
                if (data.Length >= 1)
                {
                    int ledIndex = data[0];
                    if (ledIndex is >= 1 and <= 4)
                    {
                        RegisterSpan[0x05] = (byte)(RegisterSpan[0x05] | (1 << (ledIndex - 1)));
                    }
                }
                return;
            case 0x07: // _I2C_MEM_LED_CLR
                if (data.Length >= 1)
                {
                    int ledIndex = data[0];
                    if (ledIndex is >= 1 and <= 4)
                    {
                        RegisterSpan[0x05] = (byte)(RegisterSpan[0x05] & ~(1 << (ledIndex - 1)));
                    }
                }
                return;
            case 0x3D: // _I2C_MEM_PULSE_COUNT_RESET
                if (data.Length >= 1)
                {
                    int ch = data[0];
                    if (ch is >= 1 and <= 4)
                    {
                        int baseAddr = 0x0D + (ch - 1) * 4;
                        RegisterSpan.Slice(baseAddr, 4).Clear();
                    }
                }
                return;
            case 0x3F: // _I2C_MEM_ENC_COUNT_RESET
                if (data.Length >= 1)
                {
                    int enc = data[0];
                    if (enc is >= 1 and <= 2)
                    {
                        int baseAddr = 0x25 + (enc - 1) * 4;
                        RegisterSpan.Slice(baseAddr, 4).Clear();
                    }
                }
                return;
        }

        base.OnWrite(register, data);
    }
}
