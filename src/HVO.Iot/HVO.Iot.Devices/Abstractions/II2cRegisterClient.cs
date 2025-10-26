using System;
using System.Device.I2c;

namespace HVO.Iot.Devices.Abstractions;

/// <summary>
/// Contract for register-oriented I2C clients used by HVO device drivers.
/// Provides basic byte, word, and block read/write helpers with thread-safety hooks.
/// </summary>
public interface II2cRegisterClient : IDisposable
{
    /// <summary>
    /// Gets the I2C connection settings associated with the underlying device.
    /// </summary>
    I2cConnectionSettings ConnectionSettings { get; }

    /// <summary>
    /// Gets an object suitable for <see langword="lock"/> to coordinate multi-register operations.
    /// </summary>
    object SyncRoot { get; }

    /// <summary>
    /// Reads a single byte from the specified register address.
    /// </summary>
    byte ReadByte(byte register);

    /// <summary>
    /// Reads an unsigned 16-bit value from the specified register address (little endian).
    /// </summary>
    ushort ReadUInt16(byte register);

    /// <summary>
    /// Reads an unsigned 32-bit value from the specified register address (little endian).
    /// </summary>
    uint ReadUInt32(byte register);

    /// <summary>
    /// Reads a contiguous block of bytes starting at the specified register.
    /// </summary>
    void ReadBlock(byte register, Span<byte> destination);

    /// <summary>
    /// Writes a single byte to the specified register address.
    /// </summary>
    void WriteByte(byte register, byte value);

    /// <summary>
    /// Writes an unsigned 16-bit value to the specified register address (little endian).
    /// </summary>
    void WriteUInt16(byte register, ushort value);

    /// <summary>
    /// Writes a contiguous block of bytes starting at the specified register.
    /// </summary>
    void WriteBlock(byte register, ReadOnlySpan<byte> data);
}
