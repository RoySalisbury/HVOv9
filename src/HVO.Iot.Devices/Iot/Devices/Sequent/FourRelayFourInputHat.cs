#pragma warning disable CS1591

using System;
using System.Device.I2c;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using HVO.Iot.Devices.Iot.Devices.Common;
using HVO;

namespace HVO.Iot.Devices.Iot.Devices.Sequent;

/// <summary>
/// Sequent Microsystems 4-Relay/4-Input HAT device driver with thread-safe I2C register access.
/// </summary>
public class FourRelayFourInputHat : I2cRegisterDevice
{
    /// <summary>
    /// LED operating mode.
    /// </summary>
    public enum LedMode
    {
    /// <summary>LED behavior managed automatically by the HAT.</summary>
    Auto = 0,
        /// <summary>LED behavior controlled manually via API.</summary>
        Manual = 1
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

    private readonly ILogger<FourRelayFourInputHat> _logger;

    private readonly int _hwAddress;
    private readonly int _i2cBusNo;
    private readonly Lazy<(byte Major, byte Minor)> _hardwareRevision;
    private readonly Lazy<(byte Major, byte Minor)> _softwareRevision;

    /// <summary>
    /// Initializes a new instance using a bus number and stack index (0..7).
    /// </summary>
    public FourRelayFourInputHat(int stack = 0, int i2cBus = 1, ILogger<FourRelayFourInputHat>? logger = null)
        : base(i2cBus, _CARD_BASE_ADDRESS + stack)
    {
        if (stack < 0 || stack > _STACK_LEVEL_MAX)
            throw new ArgumentOutOfRangeException(nameof(stack), "Invalid stack level!");

        _hwAddress = _CARD_BASE_ADDRESS + stack;
        _i2cBusNo = i2cBus;
        _logger = logger ?? NullLogger<FourRelayFourInputHat>.Instance;

        _hardwareRevision = new Lazy<(byte Major, byte Minor)>(GetHardwareRevision);
        _softwareRevision = new Lazy<(byte Major, byte Minor)>(GetSoftwareRevision);

        _logger.LogInformation("FourRelayFourInputHat initialized - Bus: {Bus}, Address: 0x{Addr:X2}", _i2cBusNo, _hwAddress);
    }

    /// <summary>
    /// Initializes a new instance from an existing <see cref="I2cDevice"/>.
    /// </summary>
    public FourRelayFourInputHat(I2cDevice device, ILogger<FourRelayFourInputHat>? logger = null)
        : base(device)
    {
        _ = device ?? throw new ArgumentNullException(nameof(device));
        _logger = logger ?? NullLogger<FourRelayFourInputHat>.Instance;
        _i2cBusNo = device.ConnectionSettings.BusId;
        _hwAddress = device.ConnectionSettings.DeviceAddress;

        _hardwareRevision = new Lazy<(byte Major, byte Minor)>(GetHardwareRevision);
        _softwareRevision = new Lazy<(byte Major, byte Minor)>(GetSoftwareRevision);

        _logger.LogInformation("FourRelayFourInputHat initialized (external device) - Bus: {Bus}, Address: 0x{Addr:X2}", _i2cBusNo, _hwAddress);
    }


    private (byte Major, byte Minor) GetHardwareRevision()
    {
        Span<byte> buffer = stackalloc byte[2];
        lock (Sync)
        {
            ReadBlock(_I2C_MEM_REVISION_HW_MAJOR_ADD, buffer);
        }
        return (buffer[0], buffer[1]);
    }

    private (byte Major, byte Minor) GetSoftwareRevision()
    {
        Span<byte> buffer = stackalloc byte[2];
        lock (Sync)
        {
            ReadBlock(_I2C_MEM_REVISION_MAJOR_ADD, buffer);
        }
        return (buffer[0], buffer[1]);
    }

    /// <summary>Hardware revision (Major, Minor).</summary>
    public (byte Major, byte Minor) HardwareRevision => _hardwareRevision.Value;
    /// <summary>Software/firmware revision (Major, Minor).</summary>
    public (byte Major, byte Minor) SoftwareRevision => _softwareRevision.Value;

    private short ReadRegInt16(byte register) => unchecked((short)ReadUInt16(register));

    /// <summary>
    /// Sets a relay on or off.
    /// </summary>
    /// <param name="relayIndex">Relay index 1..4.</param>
    /// <param name="isOn">True to energize the relay; false to de-energize.</param>
    public Result<bool> SetRelay(int relayIndex, bool isOn)
    {
        if (relayIndex < 1 || relayIndex > _RELAY_COUNT)
            return Result<bool>.Failure(new ArgumentOutOfRangeException(nameof(relayIndex), "Invalid relay number must be [1..4]!"));

        try
        {
            lock (Sync)
            {
                if (isOn)
                    WriteByte(_I2C_MEM_RELAY_SET, (byte)relayIndex);
                else
                    WriteByte(_I2C_MEM_RELAY_CLR, (byte)relayIndex);
            }
            _logger.LogDebug("SetRelay - Relay: {Relay}, On: {On}", relayIndex, isOn);
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "SetRelay failed - Relay: {Relay}, On: {On}", relayIndex, isOn);
            return Result<bool>.Failure(e);
        }
    }

    /// <summary>
    /// Sets a relay to <paramref name="desiredState"/> with verification and retry.
    /// </summary>
    /// <param name="relayIndex">Relay index 1..4.</param>
    /// <param name="desiredState">Desired on/off state.</param>
    /// <param name="attempts">Maximum attempts to try (>=1).</param>
    /// <param name="delayMs">Delay in milliseconds between attempts (>=0).</param>
    /// <returns>True on verified success; otherwise returns failure with context when available.</returns>
    public Result<bool> TrySetRelayWithRetry(int relayIndex, bool desiredState, int attempts = 3, int delayMs = 5)
    {
        if (relayIndex < 1 || relayIndex > _RELAY_COUNT)
            return Result<bool>.Failure(new ArgumentOutOfRangeException(nameof(relayIndex), "Invalid relay number must be [1..4]!"));

        if (attempts < 1) attempts = 1;
        if (delayMs < 0) delayMs = 0;

        Exception? lastError = null;
        for (int i = 1; i <= attempts; i++)
        {
            var setResult = SetRelay(relayIndex, desiredState);
            if (setResult.IsFailure)
            {
                lastError = setResult.Error;
            }

            // Read back relay state to verify
            var verifyResult = IsRelayOn(relayIndex);
            if (!verifyResult.IsFailure && verifyResult.Value == desiredState)
            {
                _logger.LogDebug("TrySetRelayWithRetry succeeded - Relay: {Relay}, State: {State}, Attempt: {Attempt}", relayIndex, desiredState, i);
                return true;
            }

            if (i < attempts && delayMs > 0)
            {
                try
                {
                    // Allow HAT MCU time to settle between attempts
                    Task.Delay(delayMs).Wait();
                }
                catch { /* ignore */ }
            }
        }

        if (lastError is not null)
        {
            _logger.LogError(lastError, "TrySetRelayWithRetry failed - Relay: {Relay}, State: {State}, Attempts: {Attempts}", relayIndex, desiredState, attempts);
            return Result<bool>.Failure(lastError);
        }
        else
        {
            _logger.LogError("TrySetRelayWithRetry verification failed - Relay: {Relay}, State: {State}, Attempts: {Attempts}", relayIndex, desiredState, attempts);
            return false; // indication of failure without specific exception
        }
    }

    public Result<bool> SetRelaysMask(byte mask)
    {
        try
        {
            lock (Sync)
            {
                WriteByte(_I2C_MEM_RELAY_VAL, (byte)(0x0f & mask));
            }
            _logger.LogDebug("SetRelaysMask - Mask: 0x{Mask:X2}", (byte)(0x0f & mask));
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "SetRelaysMask failed - Mask: 0x{Mask:X2}", (byte)(0x0f & mask));
            return Result<bool>.Failure(e);
        }
    }

    public Result<bool> IsRelayOn(int relayIndex)
    {
        if (relayIndex < 1 || relayIndex > _RELAY_COUNT)
            return Result<bool>.Failure(new ArgumentOutOfRangeException(nameof(relayIndex), "Invalid relay number must be [1..4]!"));
        try
        {
            int val;
            lock (Sync)
            {
                val = ReadByte(_I2C_MEM_RELAY_VAL);
            }
            return (val & (1 << (relayIndex - 1))) != 0;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "IsRelayOn failed - Relay: {Relay}", relayIndex);
            return Result<bool>.Failure(e);
        }
    }

    public Result<byte> GetRelaysMask()
    {
        try
        {
            lock (Sync)
            {
                return (byte)ReadByte(_I2C_MEM_RELAY_VAL);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetRelaysMask failed");
            return Result<byte>.Failure(e);
        }
    }

    public Result<bool> IsDigitalInputHigh(int inputIndex)
    {
        if (inputIndex < 1 || inputIndex > _IN_CH_COUNT)
            return Result<bool>.Failure(new ArgumentOutOfRangeException(nameof(inputIndex), "Invalid input channel number must be [1..4]!"));
        try
        {
            int val;
            lock (Sync)
            {
                val = ReadByte(_I2C_MEM_DIG_IN);
            }
            return (val & (1 << (inputIndex - 1))) != 0;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "IsDigitalInputHigh failed - Channel: {Channel}", inputIndex);
            return Result<bool>.Failure(e);
        }
    }

    /// <summary>
    /// Reads the digital inputs mask (bits 0..3 map to inputs 1..4).
    /// </summary>
    public Result<byte> GetDigitalInputsMask()
    {
        try
        {
            lock (Sync)
            {
                return (byte)ReadByte(_I2C_MEM_DIG_IN);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetDigitalInputsMask failed");
            return Result<byte>.Failure(e);
        }
    }

    /// <summary>
    /// Returns the high/low status of all 4 digital inputs as a tuple.
    /// </summary>
    public Result<(bool in1, bool in2, bool in3, bool in4)> GetAllDigitalInputs()
    {
        var maskResult = GetDigitalInputsMask();
        if (maskResult.IsFailure)
        {
            _logger.LogError(maskResult.Error, "GetAllDigitalInputs failed to read digital input mask");
            return Result<(bool, bool, bool, bool)>.Failure(maskResult.Error!);
        }

    byte mask = maskResult.Value; // bits 0..3 map to inputs 1..4
        var result = (
            in1: (mask & 0x01) != 0,
            in2: (mask & 0x02) != 0,
            in3: (mask & 0x04) != 0,
            in4: (mask & 0x08) != 0
        );
        return result;
    }

    public Result<bool> IsAcInputActive(int inputIndex)
    {
        if (inputIndex < 1 || inputIndex > _IN_CH_COUNT)
            return Result<bool>.Failure(new ArgumentOutOfRangeException(nameof(inputIndex), "Invalid input channel number must be [1..4]!"));
        try
        {
            int val;
            lock (Sync)
            {
                val = ReadByte(_I2C_MEM_AC_IN);
            }
            return (val & (1 << (inputIndex - 1))) != 0;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "IsAcInputActive failed - Channel: {Channel}", inputIndex);
            return Result<bool>.Failure(e);
        }
    }

    /// <summary>
    /// Reads the AC inputs mask (bits 0..3 map to inputs 1..4).
    /// </summary>
    public Result<byte> GetAcInputsMask()
    {
        try
        {
            lock (Sync)
            {
                return (byte)ReadByte(_I2C_MEM_AC_IN);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetAcInputsMask failed");
            return Result<byte>.Failure(e);
        }
    }

    /// <summary>
    /// Returns the active/inactive status of all 4 AC inputs as a tuple.
    /// </summary>
    public Result<(bool in1, bool in2, bool in3, bool in4)> GetAllAcInputs()
    {
        var maskResult = GetAcInputsMask();
        if (maskResult.IsFailure)
        {
            _logger.LogError(maskResult.Error, "GetAllAcInputs failed to read AC input mask");
            return Result<(bool, bool, bool, bool)>.Failure(maskResult.Error!);
        }

    byte mask = maskResult.Value; // bits 0..3 map to AC inputs 1..4
        var result = (
            in1: (mask & 0x01) != 0,
            in2: (mask & 0x02) != 0,
            in3: (mask & 0x04) != 0,
            in4: (mask & 0x08) != 0
        );
        return result;
    }

    public Result<bool> IsCounterEnabled(int inputIndex)
    {
        if (inputIndex < 1 || inputIndex > _IN_CH_COUNT)
            return Result<bool>.Failure(new ArgumentOutOfRangeException(nameof(inputIndex), "Invalid input channel number must be [1..4]!"));
        try
        {
            int val;
            lock (Sync)
            {
                val = ReadByte(_I2C_MEM_EDGE_ENABLE);
            }
            return (val & (1 << (inputIndex - 1))) != 0;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "IsCounterEnabled failed - Channel: {Channel}", inputIndex);
            return Result<bool>.Failure(e);
        }
    }

    public Result<bool> SetCounterEnabled(int inputIndex, bool enabled)
    {
        if (inputIndex < 1 || inputIndex > _IN_CH_COUNT)
            return Result<bool>.Failure(new ArgumentOutOfRangeException(nameof(inputIndex), "Invalid input channel number must be [1..4]!"));
        try
        {
            lock (Sync)
            {
                int val = ReadByte(_I2C_MEM_EDGE_ENABLE);
                if (enabled)
                    val |= 1 << (inputIndex - 1);
                else
                    val &= ~(1 << (inputIndex - 1));
                WriteByte(_I2C_MEM_EDGE_ENABLE, (byte)val);
            }
            _logger.LogDebug("SetCounterEnabled - Channel: {Channel}, Enabled: {Enabled}", inputIndex, enabled);
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "SetCounterEnabled failed - Channel: {Channel}, Enabled: {Enabled}", inputIndex, enabled);
            return Result<bool>.Failure(e);
        }
    }

    public Result<uint> GetPulseCount(int inputIndex)
    {
        if (inputIndex < 1 || inputIndex > _IN_CH_COUNT)
            return Result<uint>.Failure(new ArgumentOutOfRangeException(nameof(inputIndex), "Invalid input channel number must be [1..4]!"));
        try
        {
            lock (Sync)
            {
                byte address = (byte)(_I2C_MEM_PULSE_COUNT_START + (inputIndex - 1) * _COUNT_SIZE_BYTES);
                return ReadUInt32(address);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetPulseCount failed - Channel: {Channel}", inputIndex);
            return Result<uint>.Failure(e);
        }
    }

    public Result<bool> ResetPulseCount(int inputIndex)
    {
        if (inputIndex < 1 || inputIndex > _IN_CH_COUNT)
            return Result<bool>.Failure(new ArgumentOutOfRangeException(nameof(inputIndex), "Invalid input channel number must be [1..4]!"));
        try
        {
            lock (Sync)
            {
                WriteByte(_I2C_MEM_PULSE_COUNT_RESET, (byte)inputIndex);
            }
            _logger.LogDebug("ResetPulseCount - Channel: {Channel}", inputIndex);
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "ResetPulseCount failed - Channel: {Channel}", inputIndex);
            return Result<bool>.Failure(e);
        }
    }

    public Result<int> GetPulsesPerSecond(int inputIndex)
    {
        if (inputIndex < 1 || inputIndex > _IN_CH_COUNT)
            return Result<int>.Failure(new ArgumentOutOfRangeException(nameof(inputIndex), "Invalid input channel number must be [1..4]!"));
        try
        {
            lock (Sync)
            {
                byte address = (byte)(_I2C_MEM_PPS + (inputIndex - 1) * _FREQ_SIZE_BYTES);
                return ReadUInt16(address);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetPulsesPerSecond failed - Channel: {Channel}", inputIndex);
            return Result<int>.Failure(e);
        }
    }

    public Result<bool> IsEncoderEnabled(int encoderIndex)
    {
        if (encoderIndex < 1 || encoderIndex > _ENC_CH_COUNT)
            return Result<bool>.Failure(new ArgumentOutOfRangeException(nameof(encoderIndex), "Invalid encoder channel number must be [1..2]!"));
        try
        {
            int val;
            lock (Sync)
            {
                val = ReadByte(_I2C_MEM_ENC_ENABLE);
            }
            return (val & (1 << (encoderIndex - 1))) != 0;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "IsEncoderEnabled failed - Channel: {Channel}", encoderIndex);
            return Result<bool>.Failure(e);
        }
    }

    public Result<bool> SetEncoderEnabled(int encoderIndex, bool enabled)
    {
        if (encoderIndex < 1 || encoderIndex > _ENC_CH_COUNT)
            return Result<bool>.Failure(new ArgumentOutOfRangeException(nameof(encoderIndex), "Invalid encoder channel number must be [1..2]!"));
        try
        {
            lock (Sync)
            {
                int val = ReadByte(_I2C_MEM_ENC_ENABLE);
                if (enabled)
                    val |= 1 << (encoderIndex - 1);
                else
                    val &= ~(1 << (encoderIndex - 1));
                WriteByte(_I2C_MEM_ENC_ENABLE, (byte)val);
            }
            _logger.LogDebug("SetEncoderEnabled - Channel: {Channel}, Enabled: {Enabled}", encoderIndex, enabled);
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "SetEncoderEnabled failed - Channel: {Channel}, Enabled: {Enabled}", encoderIndex, enabled);
            return Result<bool>.Failure(e);
        }
    }

    public Result<int> GetEncoderCount(int encoderIndex)
    {
        if (encoderIndex < 1 || encoderIndex > _ENC_CH_COUNT)
            return Result<int>.Failure(new ArgumentOutOfRangeException(nameof(encoderIndex), "Invalid encoder channel number must be [1..2]!"));
        try
        {
            lock (Sync)
            {
                byte address = (byte)(_I2C_MEM_ENC_COUNT_START + (encoderIndex - 1) * _COUNT_SIZE_BYTES);
                return (int)ReadUInt32(address);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetEncoderCount failed - Channel: {Channel}", encoderIndex);
            return Result<int>.Failure(e);
        }
    }

    public Result<bool> ResetEncoderCount(int encoderIndex)
    {
        if (encoderIndex < 1 || encoderIndex > _ENC_CH_COUNT)
            return Result<bool>.Failure(new ArgumentOutOfRangeException(nameof(encoderIndex), "Invalid encoder channel number must be [1..2]!"));
        try
        {
            lock (Sync)
            {
                WriteByte(_I2C_MEM_ENC_COUNT_RESET, (byte)encoderIndex);
            }
            _logger.LogDebug("ResetEncoderCount - Channel: {Channel}", encoderIndex);
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "ResetEncoderCount failed - Channel: {Channel}", encoderIndex);
            return Result<bool>.Failure(e);
        }
    }

    public Result<double> GetInputFrequencyHz(int inputIndex)
    {
        if (inputIndex < 1 || inputIndex > _IN_CH_COUNT)
            return Result<double>.Failure(new ArgumentOutOfRangeException(nameof(inputIndex), "Invalid input channel number must be [1..4]!"));
        try
        {
            lock (Sync)
            {
                byte address = (byte)(_I2C_MEM_IN_FREQUENCY + (inputIndex - 1) * _FREQ_SIZE_BYTES);
                return ReadUInt16(address);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetInputFrequencyHz failed - Channel: {Channel}", inputIndex);
            return Result<double>.Failure(e);
        }
    }

    public Result<double> GetPwmDutyCyclePercent(int inputIndex)
    {
        if (inputIndex < 1 || inputIndex > _IN_CH_COUNT)
            return Result<double>.Failure(new ArgumentOutOfRangeException(nameof(inputIndex), "Invalid input channel number must be [1..4]!"));
        try
        {
            lock (Sync)
            {
                byte address = (byte)(_I2C_MEM_PWM_IN_FILL + (inputIndex - 1) * _FREQ_SIZE_BYTES);
                return ReadUInt16(address) / 100.0;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetPwmDutyCyclePercent failed - Channel: {Channel}", inputIndex);
            return Result<double>.Failure(e);
        }
    }

    public Result<double> GetCurrentAmps(int relayIndex)
    {
        if (relayIndex < 1 || relayIndex > _RELAY_COUNT)
            return Result<double>.Failure(new ArgumentOutOfRangeException(nameof(relayIndex), "Invalid relay number, number must be [1..4]!"));
        try
        {
            lock (Sync)
            {
                short val = ReadRegInt16((byte)(_I2C_CRT_IN + (relayIndex - 1) * _CRT_SIZE));
                return val / _CRT_SCALE;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetCurrentAmps failed - Relay: {Relay}", relayIndex);
            return Result<double>.Failure(e);
        }
    }

    public Result<double> GetCurrentRmsAmps(int relayIndex)
    {
        if (relayIndex < 1 || relayIndex > _RELAY_COUNT)
            return Result<double>.Failure(new ArgumentOutOfRangeException(nameof(relayIndex), "Invalid relay number, number must be [1..4]!"));
        try
        {
            lock (Sync)
            {
                short val = ReadRegInt16((byte)(_I2C_CRT_IN_RMS + (relayIndex - 1) * _CRT_SIZE));
                return val / _CRT_SCALE;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetCurrentRmsAmps failed - Relay: {Relay}", relayIndex);
            return Result<double>.Failure(e);
        }
    }

    public Result<bool> SetLedsMask(byte mask)
    {
        try
        {
            lock (Sync)
            {
                WriteByte(_I2C_MEM_LED_VAL, (byte)(0x0f & mask));
            }
            _logger.LogDebug("SetLedsMask - Mask: 0x{Mask:X2}", (byte)(0x0f & mask));
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "SetLedsMask failed - Mask: 0x{Mask:X2}", (byte)(0x0f & mask));
            return Result<bool>.Failure(e);
        }
    }

    public Result<byte> GetLedsMask()
    {
        try
        {
            lock (Sync)
            {
                return (byte)ReadByte(_I2C_MEM_LED_VAL);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetLedsMask failed");
            return Result<byte>.Failure(e);
        }
    }

    public Result<bool> SetLedMode(int ledIndex, LedMode mode)
    {
        if (ledIndex < 1 || ledIndex > _IN_CH_COUNT)
            return Result<bool>.Failure(new ArgumentOutOfRangeException(nameof(ledIndex), "LED number must be between 1 and 4."));

        try
        {
            lock (Sync)
            {
                int current = ReadByte(_I2C_MEM_LED_MODE);
                if (mode == LedMode.Manual)
                {
                    current |= 1 << (ledIndex - 1);
                }
                else
                {
                    current &= ~(1 << (ledIndex - 1));
                }
                WriteByte(_I2C_MEM_LED_MODE, (byte)current);
            }
            _logger.LogDebug("SetLedMode - Led: {Led}, Mode: {Mode}", ledIndex, mode);
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "SetLedMode failed - Led: {Led}, Mode: {Mode}", ledIndex, mode);
            return Result<bool>.Failure(e);
        }
    }

    public Result<LedMode> GetLedMode(int ledIndex)
    {
        if (ledIndex < 1 || ledIndex > _IN_CH_COUNT)
            return Result<LedMode>.Failure(new ArgumentOutOfRangeException(nameof(ledIndex), "LED number must be between 1 and 4."));

        try
        {
            int current;
            lock (Sync)
            {
                current = ReadByte(_I2C_MEM_LED_MODE);
            }
            return (current & (1 << (ledIndex - 1))) != 0 ? LedMode.Manual : LedMode.Auto;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetLedMode failed - Led: {Led}", ledIndex);
            return Result<LedMode>.Failure(e);
        }
    }

    public Result<bool> SetLed(int ledIndex, bool isOn)
    {
        if (ledIndex < 1 || ledIndex > _IN_CH_COUNT)
            return Result<bool>.Failure(new ArgumentOutOfRangeException(nameof(ledIndex), "LED number must be between 1 and 4."));

        try
        {
            lock (Sync)
            {
                var reg = isOn ? _I2C_MEM_LED_SET : _I2C_MEM_LED_CLR;
                WriteByte(reg, (byte)ledIndex);
            }
            _logger.LogDebug("SetLed - Led: {Led}, On: {On}", ledIndex, isOn);
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "SetLed failed - Led: {Led}, On: {On}", ledIndex, isOn);
            return Result<bool>.Failure(e);
        }
    }

    public Result<bool> IsLedOn(int ledIndex)
    {
        if (ledIndex < 1 || ledIndex > _IN_CH_COUNT)
            return Result<bool>.Failure(new ArgumentOutOfRangeException(nameof(ledIndex), "LED number must be between 1 and 4."));

        try
        {
            int val;
            lock (Sync)
            {
                val = ReadByte(_I2C_MEM_LED_VAL);
            }
            return (val & (1 << (ledIndex - 1))) != 0;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "IsLedOn failed - Led: {Led}", ledIndex);
            return Result<bool>.Failure(e);
        }
    }
}

#pragma warning restore CS1591
