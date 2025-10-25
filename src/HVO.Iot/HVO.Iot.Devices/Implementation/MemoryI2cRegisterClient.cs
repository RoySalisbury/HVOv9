using System;
using System.Device.I2c;
using HVO.Iot.Devices.Abstractions;

namespace HVO.Iot.Devices.Implementation;

#pragma warning disable CS1591
/// <summary>
/// In-memory implementation of <see cref="II2cRegisterClient"/> suitable for simulations and automated tests.
/// The default behaviour mimics a simple register array, while derived types can override hooks to provide
/// device-specific semantics.
/// </summary>
public abstract class MemoryI2cRegisterClient : II2cRegisterClient
{
    private readonly byte[] _registers;
    private readonly I2cConnectionSettings _connectionSettings;

    protected MemoryI2cRegisterClient(int busId, int address, int registerCount = 256)
    {
        if (registerCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(registerCount), "Register count must be positive.");
        }

        _registers = new byte[registerCount];
        _connectionSettings = new I2cConnectionSettings(busId, address);
        SyncRoot = new object();
    }

    public I2cConnectionSettings ConnectionSettings => _connectionSettings;

    public object SyncRoot { get; }

    public virtual byte ReadByte(byte register)
    {
        lock (SyncRoot)
        {
            return OnRead(register, 1).Span[0];
        }
    }

    public virtual ushort ReadUInt16(byte register)
    {
        lock (SyncRoot)
        {
            var span = OnRead(register, 2).Span;
            return (ushort)(span[0] | (span[1] << 8));
        }
    }

    public virtual uint ReadUInt32(byte register)
    {
        lock (SyncRoot)
        {
            var span = OnRead(register, 4).Span;
            return (uint)(span[0] | (span[1] << 8) | (span[2] << 16) | (span[3] << 24));
        }
    }

    public virtual void ReadBlock(byte register, Span<byte> destination)
    {
        lock (SyncRoot)
        {
            OnRead(register, destination.Length).Span.CopyTo(destination);
        }
    }

    public virtual void WriteByte(byte register, byte value)
    {
        lock (SyncRoot)
        {
            Span<byte> buffer = stackalloc byte[] { value };
            OnWrite(register, buffer);
        }
    }

    public virtual void WriteUInt16(byte register, ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        buffer[0] = (byte)(value & 0xFF);
        buffer[1] = (byte)((value >> 8) & 0xFF);
        lock (SyncRoot)
        {
            OnWrite(register, buffer);
        }
    }

    public virtual void WriteBlock(byte register, ReadOnlySpan<byte> data)
    {
        lock (SyncRoot)
        {
            OnWrite(register, data);
        }
    }

    public void Dispose()
    {
        // no unmanaged resources to release for the in-memory implementation
    }

    /// <summary>
    /// Provides direct access to the backing register array for derived classes.
    /// </summary>
    protected Span<byte> RegisterSpan => _registers;

    /// <summary>
    /// Hook invoked when a read is performed. Override to provide custom register behaviour.
    /// </summary>
    /// <returns>A buffer containing the register values to return.</returns>
    protected virtual ReadOnlyMemory<byte> OnRead(byte register, int length)
    {
        var slice = new byte[length];
        Array.Copy(_registers, register, slice, 0, length);
        return slice;
    }

    /// <summary>
    /// Hook invoked when a write is performed. Override to intercept / mutate behaviour.
    /// </summary>
    /// <param name="register">The starting register address.</param>
    /// <param name="data">Data to be written.</param>
    protected virtual void OnWrite(byte register, ReadOnlySpan<byte> data)
    {
        data.CopyTo(_registers.AsSpan(register));
    }
#pragma warning restore CS1591
}
