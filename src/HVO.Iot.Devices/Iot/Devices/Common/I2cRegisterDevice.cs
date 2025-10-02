#pragma warning disable CS1591
using System;
using System.Device.I2c;
using HVO.Iot.Devices.Abstractions;

namespace HVO.Iot.Devices.Iot.Devices.Common;

/// <summary>
/// Base class for register-oriented I2C peripherals that delegate raw bus operations to an <see cref="II2cRegisterClient"/>.
/// This design enables swapping between hardware-backed and simulated implementations while keeping device logic unchanged.
/// </summary>
public abstract class RegisterBasedI2cDevice : IDisposable
{
    private bool _disposed;

    protected RegisterBasedI2cDevice(II2cRegisterClient registerClient)
        : this(registerClient, ownsClient: false)
    {
    }

    protected RegisterBasedI2cDevice(II2cRegisterClient registerClient, bool ownsClient)
    {
        Client = registerClient ?? throw new ArgumentNullException(nameof(registerClient));
        OwnsClient = ownsClient;
    }

    protected II2cRegisterClient Client { get; }

    protected bool OwnsClient { get; }

    /// <summary>
    /// Provides access to a shared synchronization object suitable for <see langword="lock"/> statements.
    /// </summary>
    protected object Sync => Client.SyncRoot;

    /// <summary>
    /// Exposes the underlying connection settings for logging and diagnostics.
    /// </summary>
    protected I2cConnectionSettings ConnectionSettings => Client.ConnectionSettings;

    protected byte ReadByte(byte register) => Client.ReadByte(register);

    protected ushort ReadUInt16(byte register) => Client.ReadUInt16(register);

    protected uint ReadUInt32(byte register) => Client.ReadUInt32(register);

    protected void ReadBlock(byte register, Span<byte> destination) => Client.ReadBlock(register, destination);

    protected void WriteByte(byte register, byte value) => Client.WriteByte(register, value);

    protected void WriteUInt16(byte register, ushort value) => Client.WriteUInt16(register, value);

    protected void WriteBlock(byte register, ReadOnlySpan<byte> data) => Client.WriteBlock(register, data);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (OwnsClient)
        {
            Client.Dispose();
        }

        _disposed = true;
    }
}

#pragma warning restore CS1591