using System;
using System.Collections.Generic;
using HVO.Iot.Devices.Implementation;

namespace HVO.Iot.Devices.Iot.Devices.Sequent;

#pragma warning disable CS1591
/// <summary>
/// In-memory simulation of the Sequent Microsystems Four Relay / Four Input HAT used for
/// development environments and automated tests.
/// </summary>
public sealed class FourRelayFourInputHatMemoryClient : MemoryI2cRegisterClient
{
    private const byte RelayMaskRegister = 0x00;
    private const byte RelaySetRegister = 0x01;
    private const byte RelayClearRegister = 0x02;
    private const byte DigitalInputRegister = 0x03;
    private const byte LedMaskRegister = 0x05;

    private readonly List<(byte Register, byte Value)> _relayWriteLog = new();

    public FourRelayFourInputHatMemoryClient(int busId = 1, int address = 0x0E)
        : base(busId, address)
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
#pragma warning restore CS1591
