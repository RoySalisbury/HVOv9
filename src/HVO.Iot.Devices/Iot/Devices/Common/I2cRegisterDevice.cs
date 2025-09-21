#pragma warning disable CS1591
using System;
using System.Device.I2c;

namespace HVO.Iot.Devices.Iot.Devices.Common;

public abstract class I2cRegisterDevice : IDisposable
{
    protected I2cRegisterDevice(int busId, int address)
    {
        Device = I2cDevice.Create(new I2cConnectionSettings(busId, address));
        OwnsDevice = true;
    }

    protected I2cRegisterDevice(I2cDevice device)
    {
        Device = device ?? throw new ArgumentNullException(nameof(device));
        OwnsDevice = false;
    }

    protected I2cDevice Device { get; }
    protected bool OwnsDevice { get; }
    protected object Sync { get; } = new();

    public void Dispose()
    {
        if (OwnsDevice)
        {
            Device.Dispose();
        }
    }

    protected byte ReadByte(byte reg)
    {
        Span<byte> rb = stackalloc byte[1];
        Device.WriteRead(stackalloc byte[] { reg }, rb);
        return rb[0];
    }

    protected ushort ReadUInt16(byte reg)
    {
        Span<byte> rb = stackalloc byte[2];
        Device.WriteRead(stackalloc byte[] { reg }, rb);
        return (ushort)(rb[0] | (rb[1] << 8));
    }

    protected uint ReadUInt32(byte reg)
    {
        Span<byte> rb = stackalloc byte[4];
        Device.WriteRead(stackalloc byte[] { reg }, rb);
        return (uint)(rb[0] | (rb[1] << 8) | (rb[2] << 16) | (rb[3] << 24));
    }

    protected void ReadBlock(byte reg, Span<byte> destination)
    {
        Device.WriteRead(stackalloc byte[] { reg }, destination);
    }

    protected void WriteByte(byte reg, byte value)
    {
        Device.Write(stackalloc byte[] { reg, value });
    }

    protected void WriteUInt16(byte reg, ushort value)
    {
        Device.Write(stackalloc byte[] { reg, (byte)(value & 0xFF), (byte)((value >> 8) & 0xFF) });
    }

    protected void WriteBlock(byte reg, ReadOnlySpan<byte> data)
    {
        byte[] buffer = new byte[1 + data.Length];
        buffer[0] = reg;
        data.CopyTo(buffer.AsSpan(1));
        Device.Write(buffer);
    }
}

#pragma warning restore CS1591