using System;
using System.Collections.Generic;
using System.Linq;
using HVO.Iot.Devices.Iot.Devices.Sequent;
using HVO.Iot.Devices.Implementation;

namespace HVO.WebSite.RoofControllerV4.Tests.TestSupport;

/// <summary>
/// Lightweight fake for the Sequent Microsystems FourRelayFourInput HAT used across the Roof Controller tests.
/// Provides deterministic register behaviour, relay write logging, and helpers to manipulate input state.
/// </summary>
internal sealed class FakeRoofHat : FourRelayFourInputHat
{
    private readonly FakeRoofRegisterClient _client;

    public FakeRoofHat() : base(new FakeRoofRegisterClient(), ownsClient: true)
    {
        _client = (FakeRoofRegisterClient)Client;
    }

    /// <summary>
    /// Updates the raw input register to simulate electrical states for the four digital inputs.
    /// </summary>
    public void SetInputs(bool forwardLimitHigh, bool reverseLimitHigh, bool faultHigh, bool atSpeedHigh)
        => _client.SetDigitalInputs(forwardLimitHigh, reverseLimitHigh, faultHigh, atSpeedHigh);

    /// <summary>
    /// Clears the relay write log captured during test execution.
    /// </summary>
    public void ClearRelayWriteLog() => _client.ClearRelayWriteLog();

    /// <summary>
    /// Gets the accumulated relay write log (register/value pairs) for sequencing assertions.
    /// </summary>
    public IReadOnlyList<(byte Register, byte Value)> RelayWriteLog => _client.RelayWriteLog;

    /// <summary>
    /// Gets the current relay mask register (bits 0-3 represent relays 1-4).
    /// </summary>
    public byte RelayMask => _client.RelayMask;

    /// <summary>
    /// Gets the current LED mask register value (bits 0-3 map to indicator LEDs 1-4).
    /// </summary>
    public byte LedMask => _client.LedMask;

    private sealed class FakeRoofRegisterClient : MemoryI2cRegisterClient
    {
        private const byte RelayMaskRegister = 0x00;
        private const byte RelaySetRegister = 0x01;
        private const byte RelayClearRegister = 0x02;
        private const byte DigitalInputRegister = 0x03;
        private const byte LedMaskRegister = 0x05;

        private readonly List<(byte Register, byte Value)> _relayWriteLog = new();
        public FakeRoofRegisterClient()
            : base(busId: 1, address: 0x0E)
        {
        }

        public IReadOnlyList<(byte Register, byte Value)> RelayWriteLog
        {
            get
            {
                lock (SyncRoot)
                {
                    return _relayWriteLog.ToArray();
                }
            }
        }

        public byte RelayMask
        {
            get
            {
                lock (SyncRoot)
                {
                    return (byte)(RegisterSpan[RelayMaskRegister] & 0x0F);
                }
            }
        }

        public byte LedMask
        {
            get
            {
                lock (SyncRoot)
                {
                    return (byte)(RegisterSpan[LedMaskRegister] & 0x0F);
                }
            }
        }

        public void ClearRelayWriteLog()
        {
            lock (SyncRoot)
            {
                _relayWriteLog.Clear();
            }
        }

        public void SetDigitalInputs(bool in1, bool in2, bool in3, bool in4)
        {
            lock (SyncRoot)
            {
                byte mask = 0;
                if (in1) mask |= 0x01;
                if (in2) mask |= 0x02;
                if (in3) mask |= 0x04;
                if (in4) mask |= 0x08;
                RegisterSpan[DigitalInputRegister] = mask;
            }
        }

        protected override void OnWrite(byte register, ReadOnlySpan<byte> data)
        {
            lock (SyncRoot)
            {
                if (data.Length >= 1 && register is RelayMaskRegister or RelaySetRegister or RelayClearRegister)
                {
                    _relayWriteLog.Add((register, data[0]));
                }

                switch (register)
                {
                    case RelayMaskRegister:
                        RegisterSpan[RelayMaskRegister] = (byte)(data[0] & 0x0F);
                        break;
                    case RelaySetRegister:
                        if (data[0] is >= 1 and <= 4)
                        {
                            RegisterSpan[RelayMaskRegister] = (byte)(RegisterSpan[RelayMaskRegister] | (1 << (data[0] - 1)));
                        }
                        break;
                    case RelayClearRegister:
                        if (data[0] is >= 1 and <= 4)
                        {
                            RegisterSpan[RelayMaskRegister] = (byte)(RegisterSpan[RelayMaskRegister] & ~(1 << (data[0] - 1)));
                        }
                        break;
                    case LedMaskRegister:
                        RegisterSpan[LedMaskRegister] = (byte)(data[0] & 0x0F);
                        break;
                    default:
                        base.OnWrite(register, data);
                        return;
                }

                if (data.Length > 1)
                {
                    for (var i = 1; i < data.Length; i++)
                    {
                        RegisterSpan[register + i] = data[i];
                    }
                }
            }
        }
    }
}
