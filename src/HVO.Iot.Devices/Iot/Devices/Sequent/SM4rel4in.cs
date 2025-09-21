#pragma warning disable CS1591

using System;
using System.Device.I2c;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVO.Iot.Devices.Iot.Devices.Sequent;

public class SM4rel4in : IDisposable
{
    public enum LED_MODE
    {
        AUTO = 0,
        MANUAL = 1
    }

    public enum LED_STATE
    {
        OFF = 0,
        ON = 1
    }

    public const string Version = "1.0.5";
    private const int _CARD_BASE_ADDRESS = 0x0e;
    private const int _STACK_LEVEL_MAX = 7;
    private const int _IN_CH_COUNT = 4;
    private const int _RELAY_COUNT = 4;
    private const int _ENC_CH_COUNT = 2;
    private const int _FREQ_SIZE_BYTES = 2;
    private const int _COUNT_SIZE_BYTES = 4;
    private const int _CRT_SIZE = 2;

    private const byte _I2C_MEM_REVISION_HW_MAJOR_ADD = 0x78; // 120
    private const byte _I2C_MEM_REVISION_HW_MINOR_ADD = 0x79; // 121
    private const byte _I2C_MEM_REVISION_MAJOR_ADD = 0x7A;    // 122
    private const byte _I2C_MEM_REVISION_MINOR_ADD = 0x7B;    // 123
    private const byte _I2C_MEM_RELAY_VAL = 0x00;
    private const byte _I2C_MEM_RELAY_SET = 0x01;
    private const byte _I2C_MEM_RELAY_CLR = 0x02;
    private const byte _I2C_MEM_DIG_IN = 0x03;
    private const byte _I2C_MEM_AC_IN = 0x04;
    private const byte _I2C_MEM_LED_VAL = 5;
    private const byte _I2C_MEM_LED_SET = 6;
    private const byte _I2C_MEM_LED_CLR = 7;
    private const byte _I2C_MEM_LED_MODE = 8;
    private const byte _I2C_MEM_EDGE_ENABLE = 9;
    private const byte _I2C_MEM_ENC_ENABLE = 10;
    private const byte _I2C_MEM_PULSE_COUNT_START = 13;
    private const byte _I2C_MEM_ENC_COUNT_START = 37;
    private const byte _I2C_MEM_PPS = 29;
    private const byte _I2C_MEM_PULSE_COUNT_RESET = 61;
    private const byte _I2C_MEM_ENC_COUNT_RESET = 63;
    private const byte _I2C_MEM_PWM_IN_FILL = 45;
    private const byte _I2C_MEM_IN_FREQUENCY = 53;
    private const byte _I2C_CRT_IN = 72;
    private const byte _I2C_CRT_IN_RMS = 80;
    private const double _CRT_SCALE = 1000.0;

    public const int LED_AUTO = 0;
    public const int LED_MANUAL = 1;

    private readonly I2cDevice _device;
    private readonly bool _ownsDevice;
    private readonly object _sync = new();
    private readonly ILogger<SM4rel4in> _logger;

    private readonly int _hwAddress;
    private readonly int _i2cBusNo;
    private readonly Lazy<(byte Major, byte Minor)> _hardwareRevision;
    private readonly Lazy<(byte Major, byte Minor)> _softwareRevision;

    public SM4rel4in(int stack = 0, int i2cBus = 1, ILogger<SM4rel4in>? logger = null)
    {
        if (stack < 0 || stack > _STACK_LEVEL_MAX)
            throw new ArgumentOutOfRangeException(nameof(stack), "Invalid stack level!");

        _hwAddress = _CARD_BASE_ADDRESS + stack;
        _i2cBusNo = i2cBus;
        _logger = logger ?? NullLogger<SM4rel4in>.Instance;

        _device = I2cDevice.Create(new I2cConnectionSettings(_i2cBusNo, _hwAddress));
        _ownsDevice = true;

        _hardwareRevision = new Lazy<(byte Major, byte Minor)>(GetHardwareRevision);
        _softwareRevision = new Lazy<(byte Major, byte Minor)>(GetSoftwareRevision);

        _logger.LogInformation("SM4rel4in initialized - Bus: {Bus}, Address: 0x{Addr:X2}", _i2cBusNo, _hwAddress);
    }

    public SM4rel4in(I2cDevice device, ILogger<SM4rel4in>? logger = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _ownsDevice = false;
        _logger = logger ?? NullLogger<SM4rel4in>.Instance;
        _i2cBusNo = device.ConnectionSettings.BusId;
        _hwAddress = device.ConnectionSettings.DeviceAddress;

        _hardwareRevision = new Lazy<(byte Major, byte Minor)>(GetHardwareRevision);
        _softwareRevision = new Lazy<(byte Major, byte Minor)>(GetSoftwareRevision);

        _logger.LogInformation("SM4rel4in initialized (external device) - Bus: {Bus}, Address: 0x{Addr:X2}", _i2cBusNo, _hwAddress);
    }

    public void Dispose()
    {
        if (_ownsDevice)
        {
            _device.Dispose();
        }
    }

    private byte ReadRegByte(byte register)
    {
        Span<byte> buffer = stackalloc byte[1];
        _device.WriteRead(stackalloc byte[] { register }, buffer);
        return buffer[0];
    }

    private short ReadRegInt16(byte register)
    {
        Span<byte> buffer = stackalloc byte[2];
        _device.WriteRead(stackalloc byte[] { register }, buffer);
        return BitConverter.ToInt16(buffer);
    }

    private (byte Major, byte Minor) GetHardwareRevision()
    {
        Span<byte> buffer = stackalloc byte[2];
        lock (_sync)
        {
            _device.WriteRead(stackalloc byte[] { _I2C_MEM_REVISION_HW_MAJOR_ADD }, buffer);
        }
        return (buffer[0], buffer[1]);
    }

    private (byte Major, byte Minor) GetSoftwareRevision()
    {
        Span<byte> buffer = stackalloc byte[2];
        lock (_sync)
        {
            _device.WriteRead(stackalloc byte[] { _I2C_MEM_REVISION_MAJOR_ADD }, buffer);
        }
        return (buffer[0], buffer[1]);
    }

    public (byte Major, byte Minor) HardwareRevision => _hardwareRevision.Value;
    public (byte Major, byte Minor) SoftwareRevision => _softwareRevision.Value;

    private ushort ReadRegUInt16(byte register)
    {
        Span<byte> buffer = stackalloc byte[2];
        _device.WriteRead(stackalloc byte[] { register }, buffer);
        return BitConverter.ToUInt16(buffer);
    }

    private uint ReadRegUInt32(byte register)
    {
        Span<byte> buffer = stackalloc byte[4];
        _device.WriteRead(stackalloc byte[] { register }, buffer);
        return BitConverter.ToUInt32(buffer);
    }

    private void WriteReg(byte register, byte value)
    {
        _device.Write(stackalloc byte[] { register, value });
    }

    public void SetRelay(int relay, int val)
    {
        if (relay < 1 || relay > _RELAY_COUNT)
            throw new ArgumentOutOfRangeException(nameof(relay), "Invalid relay number must be [1..4]!");

        try
        {
            lock (_sync)
            {
                if (val != 0)
                    WriteReg(_I2C_MEM_RELAY_SET, (byte)relay);
                else
                    WriteReg(_I2C_MEM_RELAY_CLR, (byte)relay);
            }
            _logger.LogDebug("Relay set - Relay: {Relay}, Value: {Value}", relay, val);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "SetRelay failed - Relay: {Relay}, Value: {Value}", relay, val);
            throw new Exception("Fail to write with exception " + e.Message, e);
        }
    }

    public void SetAllRelays(int val)
    {
        try
        {
            lock (_sync)
            {
                WriteReg(_I2C_MEM_RELAY_VAL, (byte)(0x0f & val));
            }
            _logger.LogDebug("All relays set - Mask: 0x{Mask:X2}", (byte)(0x0f & val));
        }
        catch (Exception e)
        {
            _logger.LogError(e, "SetAllRelays failed - Value: {Value}", val);
            throw new Exception("Fail to write with exception " + e.Message, e);
        }
    }

    public int GetRelay(int relay)
    {
        if (relay < 1 || relay > _RELAY_COUNT)
            throw new ArgumentOutOfRangeException(nameof(relay), "Invalid relay number must be [1..4]!");
        try
        {
            int val;
            lock (_sync)
            {
                val = ReadRegByte(_I2C_MEM_RELAY_VAL);
            }
            return ((val & (1 << (relay - 1))) != 0) ? 1 : 0;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetRelay failed - Relay: {Relay}", relay);
            throw new Exception("Fail to read with exception " + e.Message, e);
        }
    }

    public int GetAllRelays()
    {
        try
        {
            lock (_sync)
            {
                return ReadRegByte(_I2C_MEM_RELAY_VAL);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetAllRelays failed");
            throw new Exception("Fail to read with exception " + e.Message, e);
        }
    }

    public int GetIn(int channel)
    {
        if (channel < 1 || channel > _IN_CH_COUNT)
            throw new ArgumentOutOfRangeException(nameof(channel), "Invalid input channel number number must be [1..4]!");
        try
        {
            int val;
            lock (_sync)
            {
                val = ReadRegByte(_I2C_MEM_DIG_IN);
            }
            return ((val & (1 << (channel - 1))) != 0) ? 1 : 0;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetIn failed - Channel: {Channel}", channel);
            throw new Exception("Fail to read with exception " + e.Message, e);
        }
    }

    public int GetAllIn()
    {
        try
        {
            lock (_sync)
            {
                return ReadRegByte(_I2C_MEM_DIG_IN);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetAllIn failed");
            throw new Exception("Fail to read with exception " + e.Message, e);
        }
    }

    public int GetAcIn(int channel)
    {
        if (channel < 1 || channel > _IN_CH_COUNT)
            throw new ArgumentOutOfRangeException(nameof(channel), "Invalid input channel number number must be [1..4]!");
        try
        {
            int val;
            lock (_sync)
            {
                val = ReadRegByte(_I2C_MEM_AC_IN);
            }
            return ((val & (1 << (channel - 1))) != 0) ? 1 : 0;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetAcIn failed - Channel: {Channel}", channel);
            throw new Exception("Fail to read with exception " + e.Message, e);
        }
    }

    public int GetAllAcIn()
    {
        try
        {
            lock (_sync)
            {
                return ReadRegByte(_I2C_MEM_AC_IN);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetAllAcIn failed");
            throw new Exception("Fail to read with exception " + e.Message, e);
        }
    }

    public int GetCountCfg(int channel)
    {
        if (channel < 1 || channel > _IN_CH_COUNT)
            throw new ArgumentOutOfRangeException(nameof(channel), "Invalid input channel number number must be [1..4]!");
        try
        {
            int val;
            lock (_sync)
            {
                val = ReadRegByte(_I2C_MEM_EDGE_ENABLE);
            }
            return ((val & (1 << (channel - 1))) != 0) ? 1 : 0;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetCountCfg failed - Channel: {Channel}", channel);
            throw new Exception("Fail to read with exception " + e.Message, e);
        }
    }

    public void SetCountCfg(int channel, int state)
    {
        if (channel < 1 || channel > _IN_CH_COUNT)
            throw new ArgumentOutOfRangeException(nameof(channel), "Invalid input channel number number must be [1..4]!");
        try
        {
            lock (_sync)
            {
                int val = ReadRegByte(_I2C_MEM_EDGE_ENABLE);
                if (state != 0)
                    val |= 1 << (channel - 1);
                else
                    val &= ~(1 << (channel - 1));
                WriteReg(_I2C_MEM_EDGE_ENABLE, (byte)val);
            }
            _logger.LogDebug("SetCountCfg - Channel: {Channel}, State: {State}", channel, state);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "SetCountCfg failed - Channel: {Channel}, State: {State}", channel, state);
            throw new Exception("Fail to write with exception " + e.Message, e);
        }
    }

    public uint GetCount(int channel)
    {
        if (channel < 1 || channel > _IN_CH_COUNT)
            throw new ArgumentOutOfRangeException(nameof(channel), "Invalid input channel number number must be [1..4]!");
        try
        {
            lock (_sync)
            {
                byte address = (byte)(_I2C_MEM_PULSE_COUNT_START + (channel - 1) * _COUNT_SIZE_BYTES);
                return ReadRegUInt32(address);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetCount failed - Channel: {Channel}", channel);
            throw new Exception("Fail to read with exception " + e.Message, e);
        }
    }

    public void ResetCount(int channel)
    {
        if (channel < 1 || channel > _IN_CH_COUNT)
            throw new ArgumentOutOfRangeException(nameof(channel), "Invalid input channel number number must be [1..4]!");
        try
        {
            lock (_sync)
            {
                WriteReg(_I2C_MEM_PULSE_COUNT_RESET, (byte)channel);
            }
            _logger.LogDebug("ResetCount - Channel: {Channel}", channel);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "ResetCount failed - Channel: {Channel}", channel);
            throw new Exception("Fail to write with exception " + e.Message, e);
        }
    }

    public ushort GetPps(int channel)
    {
        if (channel < 1 || channel > _IN_CH_COUNT)
            throw new ArgumentOutOfRangeException(nameof(channel), "Invalid input channel number number must be [1..4]!");
        try
        {
            lock (_sync)
            {
                byte address = (byte)(_I2C_MEM_PPS + (channel - 1) * _FREQ_SIZE_BYTES);
                return ReadRegUInt16(address);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetPps failed - Channel: {Channel}", channel);
            throw new Exception("Fail to read with exception " + e.Message, e);
        }
    }

    public int GetEncoderCfg(int channel)
    {
        if (channel < 1 || channel > _ENC_CH_COUNT)
            throw new ArgumentOutOfRangeException(nameof(channel), "Invalid input channel number number must be [1..2]!");
        try
        {
            int val;
            lock (_sync)
            {
                val = ReadRegByte(_I2C_MEM_ENC_ENABLE);
            }
            return ((val & (1 << (channel - 1))) != 0) ? 1 : 0;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetEncoderCfg failed - Channel: {Channel}", channel);
            throw new Exception("Fail to read with exception " + e.Message, e);
        }
    }

    public void SetEncoderCfg(int channel, int state)
    {
        if (channel < 1 || channel > _ENC_CH_COUNT)
            throw new ArgumentOutOfRangeException(nameof(channel), "Invalid input channel number number must be [1..2]!");
        try
        {
            lock (_sync)
            {
                int val = ReadRegByte(_I2C_MEM_ENC_ENABLE);
                if (state != 0)
                    val |= 1 << (channel - 1);
                else
                    val &= ~(1 << (channel - 1));
                WriteReg(_I2C_MEM_ENC_ENABLE, (byte)val);
            }
            _logger.LogDebug("SetEncoderCfg - Channel: {Channel}, State: {State}", channel, state);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "SetEncoderCfg failed - Channel: {Channel}, State: {State}", channel, state);
            throw new Exception("Fail to write with exception " + e.Message, e);
        }
    }

    public int GetEncoder(int channel)
    {
        if (channel < 1 || channel > _ENC_CH_COUNT)
            throw new ArgumentOutOfRangeException(nameof(channel), "Invalid input channel number number must be [1..2]!");
        try
        {
            lock (_sync)
            {
                byte address = (byte)(_I2C_MEM_ENC_COUNT_START + (channel - 1) * _COUNT_SIZE_BYTES);
                return (int)ReadRegUInt32(address);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetEncoder failed - Channel: {Channel}", channel);
            throw new Exception("Fail to read with exception " + e.Message, e);
        }
    }

    public void ResetEncoder(int channel)
    {
        if (channel < 1 || channel > _ENC_CH_COUNT)
            throw new ArgumentOutOfRangeException(nameof(channel), "Invalid input channel number number must be [1..2]!");
        try
        {
            lock (_sync)
            {
                WriteReg(_I2C_MEM_ENC_COUNT_RESET, (byte)channel);
            }
            _logger.LogDebug("ResetEncoder - Channel: {Channel}", channel);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "ResetEncoder failed - Channel: {Channel}", channel);
            throw new Exception("Fail to write with exception " + e.Message, e);
        }
    }

    public ushort GetFrequency(int channel)
    {
        if (channel < 1 || channel > _IN_CH_COUNT)
            throw new ArgumentOutOfRangeException(nameof(channel), "Invalid input channel number number must be [1..4]!");
        try
        {
            lock (_sync)
            {
                byte address = (byte)(_I2C_MEM_IN_FREQUENCY + (channel - 1) * _FREQ_SIZE_BYTES);
                return ReadRegUInt16(address);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetFrequency failed - Channel: {Channel}", channel);
            throw new Exception("Fail to read with exception " + e.Message, e);
        }
    }

    public double GetPwmFill(int channel)
    {
        if (channel < 1 || channel > _IN_CH_COUNT)
            throw new ArgumentOutOfRangeException(nameof(channel), "Invalid input channel number number must be [1..4]!");
        try
        {
            lock (_sync)
            {
                byte address = (byte)(_I2C_MEM_PWM_IN_FILL + (channel - 1) * _FREQ_SIZE_BYTES);
                return ReadRegUInt16(address) / 100.0;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetPwmFill failed - Channel: {Channel}", channel);
            throw new Exception("Fail to read with exception " + e.Message, e);
        }
    }

    public double GetCrt(int channel)
    {
        if (channel < 1 || channel > _RELAY_COUNT)
            throw new ArgumentOutOfRangeException(nameof(channel), "Invalid relay number, number must be [1..4]!");
        try
        {
            lock (_sync)
            {
                short val = ReadRegInt16((byte)(_I2C_CRT_IN + (channel - 1) * _CRT_SIZE));
                return val / _CRT_SCALE;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetCrt failed - Channel: {Channel}", channel);
            throw new Exception("Fail to read with exception " + e.Message, e);
        }
    }

    public double GetCrtRms(int channel)
    {
        if (channel < 1 || channel > _RELAY_COUNT)
            throw new ArgumentOutOfRangeException(nameof(channel), "Invalid relay number, number must be [1..4]!");
        try
        {
            lock (_sync)
            {
                short val = ReadRegInt16((byte)(_I2C_CRT_IN_RMS + (channel - 1) * _CRT_SIZE));
                return val / _CRT_SCALE;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetCrtRms failed - Channel: {Channel}", channel);
            throw new Exception("Fail to read with exception " + e.Message, e);
        }
    }

    [Obsolete("Use GetLedMode(byte ledNumber) returning LED_MODE instead.")]
    public int GetLedCfg(int led)
    {
        if (led < 1 || led > _IN_CH_COUNT)
            throw new ArgumentOutOfRangeException(nameof(led), "Invalid led number number must be [1..4]!");
        try
        {
            return GetLedMode((byte)led) == LED_MODE.MANUAL ? 1 : 0;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetLedCfg failed - Led: {Led}", led);
            throw new Exception("Fail to read with exception " + e.Message, e);
        }
    }

    [Obsolete("Use SetLedMode(byte ledNumber, LED_MODE mode) instead.")]
    public void SetLedCfg(int led, int mode)
    {
        if (led < 1 || led > _IN_CH_COUNT)
            throw new ArgumentOutOfRangeException(nameof(led), "Invalid led number number must be [1..4]!");
        try
        {
            SetLedMode((byte)led, mode == 0 ? LED_MODE.AUTO : LED_MODE.MANUAL);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "SetLedCfg failed - Led: {Led}, Mode: {Mode}", led, mode);
            throw new Exception("Fail to write with exception " + e.Message, e);
        }
    }

    [Obsolete("Use SetLedState(byte ledNumber, LED_STATE state) instead.")]
    public void SetLed(int led, int val)
    {
        if (led < 1 || led > _IN_CH_COUNT)
            throw new ArgumentOutOfRangeException(nameof(led), "Invalid led number number must be [1..4]!");
        try
        {
            SetLedState((byte)led, val != 0 ? LED_STATE.ON : LED_STATE.OFF);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "SetLed failed - Led: {Led}, Value: {Value}", led, val);
            throw new Exception("Fail to write with exception " + e.Message, e);
        }
    }

    public void SetAllLeds(int val)
    {
        try
        {
            lock (_sync)
            {
                WriteReg(_I2C_MEM_LED_VAL, (byte)(0x0f & val));
            }
            _logger.LogDebug("SetAllLeds - Mask: 0x{Mask:X2}", (byte)(0x0f & val));
        }
        catch (Exception e)
        {
            _logger.LogError(e, "SetAllLeds failed - Value: {Value}", val);
            throw new Exception("Fail to write with exception " + e.Message, e);
        }
    }

    [Obsolete("Use GetLedState(byte ledNumber) returning LED_STATE instead.")]
    public int GetLed(int led)
    {
        if (led < 1 || led > _IN_CH_COUNT)
            throw new ArgumentOutOfRangeException(nameof(led), "Invalid led number number must be [1..4]!");
        try
        {
            return GetLedState((byte)led) == LED_STATE.ON ? 1 : 0;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetLed failed - Led: {Led}", led);
            throw new Exception("Fail to read with exception " + e.Message, e);
        }
    }

    public int GetAllLeds()
    {
        try
        {
            lock (_sync)
            {
                return ReadRegByte(_I2C_MEM_LED_VAL);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetAllLeds failed");
            throw new Exception("Fail to read with exception " + e.Message, e);
        }
    }

    public void SetLedMode(byte ledNumber, LED_MODE mode)
    {
        if (ledNumber < 1 || ledNumber > _IN_CH_COUNT)
            throw new ArgumentOutOfRangeException(nameof(ledNumber), "LED number must be between 1 and 4.");

        try
        {
            lock (_sync)
            {
                int current = ReadRegByte(_I2C_MEM_LED_MODE);
                if (mode == LED_MODE.MANUAL)
                {
                    current |= 1 << (ledNumber - 1);
                }
                else
                {
                    current &= ~(1 << (ledNumber - 1));
                }
                WriteReg(_I2C_MEM_LED_MODE, (byte)current);
            }
            _logger.LogDebug("SetLedMode - Led: {Led}, Mode: {Mode}", ledNumber, mode);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "SetLedMode failed - Led: {Led}, Mode: {Mode}", ledNumber, mode);
            throw new Exception("Fail to write with exception " + e.Message, e);
        }
    }

    public LED_MODE GetLedMode(byte ledNumber)
    {
        if (ledNumber < 1 || ledNumber > _IN_CH_COUNT)
            throw new ArgumentOutOfRangeException(nameof(ledNumber), "LED number must be between 1 and 4.");

        try
        {
            int current;
            lock (_sync)
            {
                current = ReadRegByte(_I2C_MEM_LED_MODE);
            }
            return (current & (1 << (ledNumber - 1))) != 0 ? LED_MODE.MANUAL : LED_MODE.AUTO;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetLedMode failed - Led: {Led}", ledNumber);
            throw new Exception("Fail to read with exception " + e.Message, e);
        }
    }

    public void SetLedState(byte ledNumber, LED_STATE state)
    {
        if (ledNumber < 1 || ledNumber > _IN_CH_COUNT)
            throw new ArgumentOutOfRangeException(nameof(ledNumber), "LED number must be between 1 and 4.");

        try
        {
            lock (_sync)
            {
                var reg = state == LED_STATE.ON ? _I2C_MEM_LED_SET : _I2C_MEM_LED_CLR;
                WriteReg(reg, ledNumber);
            }
            _logger.LogDebug("SetLedState - Led: {Led}, State: {State}", ledNumber, state);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "SetLedState failed - Led: {Led}, State: {State}", ledNumber, state);
            throw new Exception("Fail to write with exception " + e.Message, e);
        }
    }

    public LED_STATE GetLedState(byte ledNumber)
    {
        if (ledNumber < 1 || ledNumber > _IN_CH_COUNT)
            throw new ArgumentOutOfRangeException(nameof(ledNumber), "LED number must be between 1 and 4.");

        try
        {
            int val;
            lock (_sync)
            {
                val = ReadRegByte(_I2C_MEM_LED_VAL);
            }
            return (val & (1 << (ledNumber - 1))) != 0 ? LED_STATE.ON : LED_STATE.OFF;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetLedState failed - Led: {Led}", ledNumber);
            throw new Exception("Fail to read with exception " + e.Message, e);
        }
    }
}

public static class I2cDeviceExtensions
{
    public static byte ReadByte(this I2cDevice device, byte register)
    {
        Span<byte> buffer = stackalloc byte[1];
        device.WriteRead(stackalloc byte[] { register }, buffer);
        return buffer[0];
    }
}

#pragma warning restore CS1591
