#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

using System;
using System.Device.I2c;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using HVO.Iot.Devices.Abstractions;
using HVO.Iot.Devices.Iot.Devices.Common;
using HVO.Iot.Devices.Implementation;
using HVO;

namespace HVO.Iot.Devices.Iot.Devices.Sequent;

public class WatchdogBatteryHat : RegisterBasedI2cDevice, IWatchdogBatteryHat
{
    private const byte HW_ADD = 0x30;

    private const byte RELOAD_ADD = 0x00;
    private const byte RELOAD_KEY = 0xCA;

    private const byte WRITE_INTERVAL_ADD = 0x01;
    private const byte READ_INTERVAL_ADD = 0x03;

    private const byte WRITE_INITIAL_INTERVAL_ADD = 0x05;
    private const byte READ_INITIAL_INTERVAL_ADD = 0x07;

    private const byte RESETS_COUNT_ADD = 0x09;
    private const byte CLEAR_RESET_COUNT_ADD = 0x0b;
    private const byte V_IN_ADD = 0x0c;

    private const byte POWER_OFF_INTERVAL_SET_ADD = 14;
    private const byte POWER_OFF_INTERVAL_GET_ADD = 18;
    private const byte V_BAT_ADD = 22;
    private const byte V_OUT_ADD = 24;
    private const byte TEMP_ADD = 26;
    private const byte CHARGE_STAT_ADD = 27;
    private const byte POWER_OFF_ON_BATTERY_ADD = 28;
    private const byte POWER_SW_USAGE_ADD = 29;
    private const byte POWER_SW_STATUS_ADD = 30;
    private const byte POWER_SW_INT_OUT_ADD = 48;

    private const int WDT_MAX_POWER_OFF_INTERVAL = 31 * 24 * 3600;

    private const byte I2C_RTC_YEAR_ADD = 31;
    private const byte I2C_RTC_MONTH_ADD = 32;
    private const byte I2C_RTC_DAY_ADD = 33;
    private const byte I2C_RTC_HOUR_ADD = 34;
    private const byte I2C_RTC_MINUTE_ADD = 35;
    private const byte I2C_RTC_SECOND_ADD = 36;
    private const byte I2C_RTC_SET_YEAR_ADD = 37;
    private const byte I2C_RTC_SET_MONTH_ADD = 38;
    private const byte I2C_RTC_SET_DAY_ADD = 39;
    private const byte I2C_RTC_SET_HOUR_ADD = 40;
    private const byte I2C_RTC_SET_MINUTE_ADD = 41;
    private const byte I2C_RTC_SET_SECOND_ADD = 42;
    private const byte I2C_RTC_CMD_ADD = 43;

    private readonly ILogger<WatchdogBatteryHat> _logger;

    private readonly int _i2cBusId;
    private readonly int _i2cAddress;

    public WatchdogBatteryHat(int i2cBusId = 1, byte address = HW_ADD, ILogger<WatchdogBatteryHat>? logger = null, int postTransactionDelayMs = 15)
        : this(new I2cRegisterClient(i2cBusId, address, postTransactionDelayMs), ownsClient: true, logger: logger, initializationSource: null)
    {
    }

    public WatchdogBatteryHat(I2cDevice device, ILogger<WatchdogBatteryHat>? logger = null, int postTransactionDelayMs = 15)
        : this(new I2cRegisterClient(device, ownsDevice: false, postTransactionDelayMs), ownsClient: false, logger: logger, initializationSource: "external device")
    {
    }

    public WatchdogBatteryHat(II2cRegisterClient registerClient, ILogger<WatchdogBatteryHat>? logger = null)
        : this(registerClient, ownsClient: false, logger: logger, initializationSource: "injected register client")
    {
    }

    public WatchdogBatteryHat(II2cRegisterClient registerClient, bool ownsClient, ILogger<WatchdogBatteryHat>? logger = null)
        : this(registerClient, ownsClient, logger, initializationSource: ownsClient ? null : "custom register client")
    {
    }

    private WatchdogBatteryHat(II2cRegisterClient registerClient, bool ownsClient, ILogger<WatchdogBatteryHat>? logger, string? initializationSource)
        : base(registerClient, ownsClient)
    {
        _logger = logger ?? NullLogger<WatchdogBatteryHat>.Instance;
        _i2cBusId = ConnectionSettings.BusId;
        _i2cAddress = ConnectionSettings.DeviceAddress;

        string suffix = string.IsNullOrWhiteSpace(initializationSource) ? string.Empty : $" ({initializationSource})";
        _logger.LogInformation("WatchdogBatteryHat initialized{InitializationSuffix} - Bus: {Bus}, Address: 0x{Addr:X2}", suffix, _i2cBusId, _i2cAddress);
    }

    public Result<int> GetWatchdogPeriodSeconds()
    {
        try
        {
            lock (Sync)
            {
                return Result<int>.Success(ReadUInt16(READ_INTERVAL_ADD));
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetWatchdogPeriodSeconds failed");
            return Result<int>.Failure(e);
        }
    }

    public Result<bool> SetWatchdogPeriodSeconds(int seconds)
    {
        if (seconds < 1) seconds = 65001;
        try
        {
            lock (Sync)
            {
                WriteUInt16(WRITE_INTERVAL_ADD, (ushort)seconds);
            }
            _logger.LogDebug("SetWatchdogPeriodSeconds - Seconds: {Seconds}", seconds);
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "SetWatchdogPeriodSeconds failed - Seconds: {Seconds}", seconds);
            return Result<bool>.Failure(e);
        }
    }

    public Result<bool> ReloadWatchdog()
    {
        try
        {
            lock (Sync)
            {
                WriteByte(RELOAD_ADD, RELOAD_KEY);
            }
            _logger.LogInformation("ReloadWatchdog executed");
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Reload failed");
            return Result<bool>.Failure(e);
        }
    }

    public Result<bool> SetDefaultWatchdogPeriodSeconds(int seconds)
    {
        if (seconds <= 10 || seconds >= 65000)
            return Result<bool>.Failure(new ArgumentOutOfRangeException(nameof(seconds), "Seconds must be within (10, 65000)."));

        try
        {
            lock (Sync)
            {
                WriteUInt16(WRITE_INITIAL_INTERVAL_ADD, (ushort)seconds);
            }
            _logger.LogDebug("SetDefaultWatchdogPeriodSeconds - Seconds: {Seconds}", seconds);
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "SetDefaultWatchdogPeriodSeconds failed - Seconds: {Seconds}", seconds);
            return Result<bool>.Failure(e);
        }
    }

    public Result<int> GetDefaultWatchdogPeriodSeconds()
    {
        try
        {
            lock (Sync)
            {
                return Result<int>.Success(ReadUInt16(READ_INITIAL_INTERVAL_ADD));
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetDefaultWatchdogPeriodSeconds failed");
            return Result<int>.Failure(e);
        }
    }

    public Result<bool> SetPowerOffIntervalSeconds(int seconds)
    {
        if (seconds <= 2 || seconds >= WDT_MAX_POWER_OFF_INTERVAL)
            return Result<bool>.Failure(new ArgumentOutOfRangeException(nameof(seconds), "Seconds must be within (2, WDT_MAX_POWER_OFF_INTERVAL)."));

        try
        {
            lock (Sync)
            {
                Span<byte> data = stackalloc byte[4];
                data[0] = (byte)(seconds & 0xff);
                data[1] = (byte)((seconds >> 8) & 0xff);
                data[2] = (byte)((seconds >> 16) & 0xff);
                data[3] = (byte)((seconds >> 24) & 0xff);
                WriteBlock(POWER_OFF_INTERVAL_SET_ADD, data);
            }
            _logger.LogDebug("SetPowerOffIntervalSeconds - Seconds: {Seconds}", seconds);
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "SetPowerOffIntervalSeconds failed - Seconds: {Seconds}", seconds);
            return Result<bool>.Failure(e);
        }
    }

    public Result<int> GetPowerOffIntervalSeconds()
    {
        try
        {
            lock (Sync)
            {
                Span<byte> readBuffer = stackalloc byte[4];
                ReadBlock(POWER_OFF_INTERVAL_GET_ADD, readBuffer);
                return Result<int>.Success(readBuffer[0] + (readBuffer[1] << 8) + (readBuffer[2] << 16) + (readBuffer[3] << 24));
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetPowerOffIntervalSeconds failed");
            return Result<int>.Failure(e);
        }
    }

    public Result<int> GetWatchdogResetCount()
    {
        try
        {
            lock (Sync)
            {
                return Result<int>.Success(ReadUInt16(RESETS_COUNT_ADD));
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetWatchdogResetCount failed");
            return Result<int>.Failure(e);
        }
    }

    public Result<double> GetInputVoltageVin()
    {
        try
        {
            lock (Sync)
            {
                int val = ReadUInt16(V_IN_ADD);
                return Result<double>.Success(val / 1000.0);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetInputVoltageVin failed");
            return Result<double>.Failure(e);
        }
    }

    public Result<double> GetSystemVoltageVout()
    {
        try
        {
            lock (Sync)
            {
                int val = ReadUInt16(V_OUT_ADD);
                return Result<double>.Success(val / 1000.0);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetSystemVoltageVout failed");
            return Result<double>.Failure(e);
        }
    }

    public Result<double> GetBatteryVoltageVbat()
    {
        try
        {
            lock (Sync)
            {
                int val = ReadUInt16(V_BAT_ADD);
                return Result<double>.Success(val / 1000.0);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetBatteryVoltageVbat failed");
            return Result<double>.Failure(e);
        }
    }

    public Result<int> GetTemperatureCelsius()
    {
        try
        {
            lock (Sync)
            {
                return Result<int>.Success(ReadByte(TEMP_ADD));
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetTemperatureCelsius failed");
            return Result<int>.Failure(e);
        }
    }

    public Result<byte> GetChargerStatusRaw()
    {
        try
        {
            lock (Sync)
            {
                return Result<byte>.Success((byte)(ReadByte(CHARGE_STAT_ADD) & 0x0f));
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetChargerStatusRaw failed");
            return Result<byte>.Failure(e);
        }
    }

    public Result<bool?> GetRepowerOnBattery()
    {
        try
        {
            lock (Sync)
            {
                int stat = ReadByte(CHARGE_STAT_ADD) & 0xf0;
                if (stat > 0)
                {
                    int val = ReadByte(POWER_OFF_ON_BATTERY_ADD);
                    return Result<bool?>.Success(val == 0);
                }
                return Result<bool?>.Success(null);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetRepowerOnBattery failed");
            return Result<bool?>.Failure(e);
        }
    }

    public Result<bool> SetRepowerOnBattery(bool enable)
    {
        try
        {
            lock (Sync)
            {
                int stat = ReadByte(CHARGE_STAT_ADD) & 0xf0;
                if (stat > 0)
                {
                    WriteByte(POWER_OFF_ON_BATTERY_ADD, enable ? (byte)0 : (byte)1);
                    return true;
                }
                return Result<bool>.Failure(new InvalidOperationException("Charger status not available."));
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "SetRepowerOnBattery failed - Enable: {Enable}", enable);
            return Result<bool>.Failure(e);
        }
    }

    public Result<int> GetPowerButtonEnableRaw()
    {
        try
        {
            lock (Sync)
            {
                int stat = ReadByte(CHARGE_STAT_ADD) & 0xf0;
                if (stat > 0x10)
                {
                    return Result<int>.Success(ReadByte(POWER_SW_USAGE_ADD));
                }
                return Result<int>.Success(0);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetPowerButtonEnableRaw failed");
            return Result<int>.Failure(e);
        }
    }

    public Result<bool> SetPowerButtonEnable(bool enable)
    {
        try
        {
            lock (Sync)
            {
                int stat = ReadByte(CHARGE_STAT_ADD) & 0xf0;
                if (stat > 0x10)
                {
                    WriteByte(POWER_SW_USAGE_ADD, enable ? (byte)1 : (byte)0);
                    return true;
                }
                return Result<bool>.Failure(new InvalidOperationException("Power button enable not supported."));
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "SetPowerButtonEnable failed - Enable: {Enable}", enable);
            return Result<bool>.Failure(e);
        }
    }

    public Result<int> GetPowerButtonStatusRaw()
    {
        try
        {
            lock (Sync)
            {
                int stat = ReadByte(CHARGE_STAT_ADD) & 0xf0;
                if (stat > 0x10)
                {
                    return Result<int>.Success(ReadByte(POWER_SW_STATUS_ADD));
                }
                return Result<int>.Success(0);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetPowerButtonStatusRaw failed");
            return Result<int>.Failure(e);
        }
    }

    public Result<bool> ClearPowerButtonStatus()
    {
        try
        {
            lock (Sync)
            {
                WriteByte(POWER_SW_STATUS_ADD, 0);
            }
            return true;
        }
        catch (Exception)
        {
            return Result<bool>.Failure(new InvalidOperationException("Failed to clear power button status."));
        }
    }

    public Result<int> GetPowerButtonStatus()
    {
        try
        {
            lock (Sync)
            {
                int stat = ReadByte(CHARGE_STAT_ADD) & 0xf0;
                if (stat > 0x10)
                {
                    return Result<int>.Success(ReadByte(POWER_SW_STATUS_ADD));
                }
                return Result<int>.Success(0);
            }
        }
        catch (Exception)
        {
            return Result<int>.Failure(new InvalidOperationException("Failed to read power button status."));
        }
    }

    public Result<bool> SetPowerButtonStatus(int value)
    {
        value = Math.Clamp(value, 0, 1);
        try
        {
            lock (Sync)
            {
                WriteByte(POWER_SW_STATUS_ADD, (byte)value);
            }
            _logger.LogDebug("SetPowerButtonStatus - Value: {Value}", value);
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "SetPowerButtonStatus failed - Value: {Value}", value);
            return Result<bool>.Failure(e);
        }
    }

    public Result<int> GetPowerButtonInterruptEnableRaw()
    {
        try
        {
            lock (Sync)
            {
                int stat = ReadByte(CHARGE_STAT_ADD) & 0xf0;
                if (stat > 0x10)
                {
                    return Result<int>.Success(ReadByte(POWER_SW_INT_OUT_ADD));
                }
                return Result<int>.Success(0);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetPowerButtonInterruptEnableRaw failed");
            return Result<int>.Failure(e);
        }
    }

    public Result<bool> SetPowerButtonInterruptEnable(bool enable)
    {
        try
        {
            lock (Sync)
            {
                int stat = ReadByte(CHARGE_STAT_ADD) & 0xf0;
                if (stat > 0x10)
                {
                    WriteByte(POWER_SW_INT_OUT_ADD, enable ? (byte)1 : (byte)0);
                    return true;
                }
                return Result<bool>.Failure(new InvalidOperationException("Power button interrupt not supported."));
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "SetPowerButtonInterruptEnable failed - Enable: {Enable}", enable);
            return Result<bool>.Failure(e);
        }
    }

    public Result<DateTime> GetRtc()
    {
        try
        {
            lock (Sync)
            {
                Span<byte> buff = stackalloc byte[6];
                ReadBlock(I2C_RTC_YEAR_ADD, buff);
                return Result<DateTime>.Success(new DateTime(2000 + buff[0], buff[1], buff[2], buff[3], buff[4], buff[5], DateTimeKind.Local));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetRtc failed");
            return Result<DateTime>.Failure(new InvalidOperationException("Could not read RTC: " + ex.Message, ex));
        }
    }

    public Result<bool> SetRtc(DateTime dateTime)
    {
        int y = dateTime.Year;
        int mo = dateTime.Month;
        int d = dateTime.Day;
        int h = dateTime.Hour;
        int m = dateTime.Minute;
        int s = dateTime.Second;
        if (y > 2000) y -= 2000;
        if (y < 0 || y > 255) return Result<bool>.Failure(new ArgumentOutOfRangeException(nameof(dateTime), "Invalid year in DateTime!"));
        Span<byte> buff = stackalloc byte[] { (byte)y, (byte)mo, (byte)d, (byte)h, (byte)m, (byte)s, 0xaa };

        try
        {
            lock (Sync)
            {
                WriteBlock(I2C_RTC_SET_YEAR_ADD, buff);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SetRtc failed");
            return Result<bool>.Failure(new InvalidOperationException("Could not set RTC: " + ex.Message, ex));
        }
        return true;
    }
}

#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
