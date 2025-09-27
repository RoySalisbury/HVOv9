using System;
using System.Collections.Generic;
using System.Device.I2c;
using System.Linq;
using HVO.Iot.Devices.Iot.Devices.Sequent;

namespace HVO.WebSite.RoofControllerV4.Tests.TestSupport;

/// <summary>
/// Lightweight fake for the Sequent Microsystems FourRelayFourInput HAT used across the Roof Controller tests.
/// Provides deterministic register behaviour, relay write logging, and helpers to manipulate input state.
/// </summary>
internal sealed class FakeRoofHat : FourRelayFourInputHat
{
    private readonly FakeRoofI2cDevice _device;

    public FakeRoofHat() : base(new FakeRoofI2cDevice())
    {
        _device = (FakeRoofI2cDevice)Device;
    }

    /// <summary>
    /// Updates the raw input register to simulate electrical states for the four digital inputs.
    /// </summary>
    public void SetInputs(bool forwardLimitHigh, bool reverseLimitHigh, bool faultHigh, bool atSpeedHigh)
        => _device.SetDigitalInputs(forwardLimitHigh, reverseLimitHigh, faultHigh, atSpeedHigh);

    /// <summary>
    /// Clears the relay write log captured during test execution.
    /// </summary>
    public void ClearRelayWriteLog() => _device.ClearRelayWriteLog();

    /// <summary>
    /// Gets the accumulated relay write log (register/value pairs) for sequencing assertions.
    /// </summary>
    public IReadOnlyList<(byte Register, byte Value)> RelayWriteLog => _device.RelayWriteLog;

    /// <summary>
    /// Gets the current relay mask register (bits 0-3 represent relays 1-4).
    /// </summary>
    public byte RelayMask => _device.RelayMask;

    /// <summary>
    /// Gets the current LED mask register value (bits 0-3 map to indicator LEDs 1-4).
    /// </summary>
    public byte LedMask => _device.LedMask;

    private sealed class FakeRoofI2cDevice : I2cDevice
    {
        private const byte RelayMaskRegister = 0x00;
        private const byte RelaySetRegister = 0x01;
        private const byte RelayClearRegister = 0x02;
        private const byte DigitalInputRegister = 0x03;
        private const byte LedMaskRegister = 0x05;

        private readonly byte[] _registers = new byte[256];
        private readonly List<(byte Register, byte Value)> _relayWriteLog = new();
        private readonly object _syncRoot = new();

        public override I2cConnectionSettings ConnectionSettings { get; } = new(1, 0x0E);

        public IReadOnlyList<(byte Register, byte Value)> RelayWriteLog
        {
            get
            {
                lock (_syncRoot)
                {
                    return _relayWriteLog.ToArray();
                }
            }
        }

        public byte RelayMask
        {
            get
            {
                lock (_syncRoot)
                {
                    return (byte)(_registers[RelayMaskRegister] & 0x0F);
                }
            }
        }

        public byte LedMask
        {
            get
            {
                lock (_syncRoot)
                {
                    return (byte)(_registers[LedMaskRegister] & 0x0F);
                }
            }
        }

        public void ClearRelayWriteLog()
        {
            lock (_syncRoot)
            {
                _relayWriteLog.Clear();
            }
        }

        public void SetDigitalInputs(bool in1, bool in2, bool in3, bool in4)
        {
            lock (_syncRoot)
            {
                byte mask = 0;
                if (in1) mask |= 0x01;
                if (in2) mask |= 0x02;
                if (in3) mask |= 0x04;
                if (in4) mask |= 0x08;
                _registers[DigitalInputRegister] = mask;
            }
        }

        public override void Read(Span<byte> buffer) => throw new NotSupportedException();

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            lock (_syncRoot)
            {
                if (buffer.Length < 2)
                {
                    return;
                }

                var register = buffer[0];
                var value = buffer[1];

                if (register is RelayMaskRegister or RelaySetRegister or RelayClearRegister)
                {
                    _relayWriteLog.Add((register, value));
                }

                switch (register)
                {
                    case RelayMaskRegister:
                        _registers[RelayMaskRegister] = (byte)(value & 0x0F);
                        break;
                    case RelaySetRegister:
                        if (value is >= 1 and <= 4)
                        {
                            _registers[RelayMaskRegister] = (byte)(_registers[RelayMaskRegister] | (1 << (value - 1)));
                        }
                        break;
                    case RelayClearRegister:
                        if (value is >= 1 and <= 4)
                        {
                            _registers[RelayMaskRegister] = (byte)(_registers[RelayMaskRegister] & ~(1 << (value - 1)));
                        }
                        break;
                    case LedMaskRegister:
                        _registers[LedMaskRegister] = (byte)(value & 0x0F);
                        break;
                    default:
                        _registers[register] = value;
                        break;
                }

                for (var i = 2; i < buffer.Length; i++)
                {
                    _registers[register + i - 1] = buffer[i];
                }
            }
        }

        public override void WriteRead(ReadOnlySpan<byte> writeBuffer, Span<byte> readBuffer)
        {
            lock (_syncRoot)
            {
                var register = writeBuffer[0];
                for (var i = 0; i < readBuffer.Length; i++)
                {
                    readBuffer[i] = _registers[register + i];
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            // No unmanaged resources; nothing to dispose.
        }
    }
}
