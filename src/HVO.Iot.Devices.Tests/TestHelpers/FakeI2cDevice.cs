#pragma warning disable CS1591
using System;
using System.Device.I2c;

namespace HVO.Iot.Devices.Tests.TestHelpers;

/// <summary>
/// In-memory fake I2C device that emulates the subset of behavior required by FourRelayFourInputHat.
/// Supports register based Write/Read with special handling for relay/LED set & clear registers.
/// </summary>
public class FakeI2cDevice : I2cDevice
{
    private readonly byte[] _regs = new byte[256];
    private bool _disposed;
    public override I2cConnectionSettings ConnectionSettings { get; }

    public FakeI2cDevice(int busId = 1, int address = 0x0e)
    {
        ConnectionSettings = new I2cConnectionSettings(busId, address);
    }

    public void SetRegister(byte reg, byte value) => _regs[reg] = value;
    public void SetRegisterBytes(byte startReg, ReadOnlySpan<byte> data) => data.CopyTo(_regs.AsSpan(startReg));
    public byte GetRegister(byte reg) => _regs[reg];

    public override void Read(Span<byte> buffer)
    {
        throw new NotSupportedException("Direct sequential Read not used by driver (uses WriteRead)." );
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FakeI2cDevice));
        if (buffer.Length < 2)
            throw new ArgumentException("Write buffer must include register + data", nameof(buffer));
        byte reg = buffer[0];
        ReadOnlySpan<byte> data = buffer[1..];

        // Special handling matching HAT semantics
        switch (reg)
        {
            case 0x01: // _I2C_MEM_RELAY_SET
                if (data.Length >= 1)
                {
                    int relayIndex = data[0];
                    if (relayIndex is >=1 and <=4)
                        _regs[0x00] = (byte)(_regs[0x00] | (1 << (relayIndex - 1))); // modify relay value register
                }
                return;
            case 0x02: // _I2C_MEM_RELAY_CLR
                if (data.Length >= 1)
                {
                    int relayIndex = data[0];
                    if (relayIndex is >=1 and <=4)
                        _regs[0x00] = (byte)(_regs[0x00] & ~(1 << (relayIndex - 1)));
                }
                return;
            case 0x06: // _I2C_MEM_LED_SET
                if (data.Length >= 1)
                {
                    int ledIndex = data[0];
                    if (ledIndex is >=1 and <=4)
                        _regs[0x05] = (byte)(_regs[0x05] | (1 << (ledIndex - 1))); // LED value register 0x05
                }
                return;
            case 0x07: // _I2C_MEM_LED_CLR
                if (data.Length >= 1)
                {
                    int ledIndex = data[0];
                    if (ledIndex is >=1 and <=4)
                        _regs[0x05] = (byte)(_regs[0x05] & ~(1 << (ledIndex - 1)));
                }
                return;
            case 0x3D: // _I2C_MEM_PULSE_COUNT_RESET
                if (data.Length >= 1)
                {
                    int ch = data[0];
                    if (ch is >=1 and <=4)
                    {
                        int baseAddr = 0x0D + (ch - 1) * 4; // _I2C_MEM_PULSE_COUNT_START
                        for (int i = 0; i < 4; i++) _regs[baseAddr + i] = 0;
                    }
                }
                return;
            case 0x3F: // _I2C_MEM_ENC_COUNT_RESET
                if (data.Length >= 1)
                {
                    int enc = data[0];
                    if (enc is >=1 and <=2)
                    {
                        int baseAddr = 0x25 + (enc - 1) * 4; // _I2C_MEM_ENC_COUNT_START
                        for (int i = 0; i < 4; i++) _regs[baseAddr + i] = 0;
                    }
                }
                return;
        }

        // Generic: write sequentially starting at reg
        for (int i = 0; i < data.Length; i++)
        {
            _regs[reg + i] = data[i];
        }
    }

    public override void WriteRead(ReadOnlySpan<byte> writeBuffer, Span<byte> readBuffer)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FakeI2cDevice));
        if (writeBuffer.Length != 1)
            throw new ArgumentException("WriteRead expects single register address in writeBuffer");
        byte reg = writeBuffer[0];
        for (int i = 0; i < readBuffer.Length; i++)
        {
            readBuffer[i] = _regs[reg + i];
        }
    }

    protected override void Dispose(bool disposing)
    {
        _disposed = true;
    }
}
#pragma warning restore CS1591
