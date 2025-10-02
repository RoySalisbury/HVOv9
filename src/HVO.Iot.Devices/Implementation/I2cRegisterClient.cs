using System;
using System.Device.I2c;
using System.Threading;
using HVO.Iot.Devices.Abstractions;

namespace HVO.Iot.Devices.Implementation;

#pragma warning disable CS1591
/// <summary>
/// Default implementation of <see cref="II2cRegisterClient"/> that wraps a <see cref="I2cDevice"/> instance.
/// Handles post-transaction delays that many Sequent Microsystems devices require to settle.
/// </summary>
public sealed class I2cRegisterClient : II2cRegisterClient
{
    private readonly I2cDevice _device;
    private readonly bool _ownsDevice;
    private readonly int _postTransactionDelayMs;
    private bool _disposed;

    /// <summary>
    /// Creates a new client by opening the device for the specified bus and address.
    /// </summary>
    public I2cRegisterClient(int busId, int address, int postTransactionDelayMs = 15)
        : this(I2cDevice.Create(new I2cConnectionSettings(busId, address)), ownsDevice: true, postTransactionDelayMs)
    {
    }

    /// <summary>
    /// Creates a new client using an existing <see cref="I2cDevice"/> instance.
    /// </summary>
    public I2cRegisterClient(I2cDevice device, bool ownsDevice = false, int postTransactionDelayMs = 15)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _ownsDevice = ownsDevice;
        _postTransactionDelayMs = postTransactionDelayMs < 0 ? 0 : postTransactionDelayMs;
        SyncRoot = new object();
    }

    public I2cConnectionSettings ConnectionSettings => _device.ConnectionSettings;

    public object SyncRoot { get; }

    public byte ReadByte(byte register)
    {
        Span<byte> rb = stackalloc byte[1];
        _device.WriteRead(stackalloc byte[] { register }, rb);
        Delay();
        return rb[0];
    }

    public ushort ReadUInt16(byte register)
    {
        Span<byte> rb = stackalloc byte[2];
        _device.WriteRead(stackalloc byte[] { register }, rb);
        Delay();
        return (ushort)(rb[0] | (rb[1] << 8));
    }

    public uint ReadUInt32(byte register)
    {
        Span<byte> rb = stackalloc byte[4];
        _device.WriteRead(stackalloc byte[] { register }, rb);
        Delay();
        return (uint)(rb[0] | (rb[1] << 8) | (rb[2] << 16) | (rb[3] << 24));
    }

    public void ReadBlock(byte register, Span<byte> destination)
    {
        _device.WriteRead(stackalloc byte[] { register }, destination);
        Delay();
    }

    public void WriteByte(byte register, byte value)
    {
        _device.Write(stackalloc byte[] { register, value });
        Delay();
    }

    public void WriteUInt16(byte register, ushort value)
    {
        _device.Write(stackalloc byte[] { register, (byte)(value & 0xFF), (byte)((value >> 8) & 0xFF) });
        Delay();
    }

    public void WriteBlock(byte register, ReadOnlySpan<byte> data)
    {
        byte[] buffer = new byte[1 + data.Length];
        buffer[0] = register;
        data.CopyTo(buffer.AsSpan(1));
        _device.Write(buffer);
        Delay();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_ownsDevice)
        {
            _device.Dispose();
        }

        _disposed = true;
    }

    private void Delay()
    {
        if (_postTransactionDelayMs > 0)
        {
            Thread.Sleep(_postTransactionDelayMs);
        }
    }
}
#pragma warning restore CS1591
