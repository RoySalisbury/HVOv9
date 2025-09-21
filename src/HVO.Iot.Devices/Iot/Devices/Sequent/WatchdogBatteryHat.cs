#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

using System;
using System.Device.I2c;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using HVO.Iot.Devices.Iot.Devices.Common;

namespace HVO.Iot.Devices.Iot.Devices.Sequent;

public class WatchdogBatteryHat : I2cRegisterDevice
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

    public WatchdogBatteryHat(int i2cBusId = 1, byte address = HW_ADD, ILogger<WatchdogBatteryHat>? logger = null)
        : base(i2cBusId, address)
    {
        _i2cBusId = i2cBusId;
        _i2cAddress = address;
        _logger = logger ?? NullLogger<WatchdogBatteryHat>.Instance;

        _logger.LogInformation("WatchdogBatteryHat initialized - Bus: {Bus}, Address: 0x{Addr:X2}", _i2cBusId, _i2cAddress);
    }

    public WatchdogBatteryHat(I2cDevice device, ILogger<WatchdogBatteryHat>? logger = null)
        : base(device)
    {
        _ = device ?? throw new ArgumentNullException(nameof(device));
        _logger = logger ?? NullLogger<WatchdogBatteryHat>.Instance;
        _i2cBusId = device.ConnectionSettings.BusId;
        _i2cAddress = device.ConnectionSettings.DeviceAddress;

        _logger.LogInformation("WatchdogBatteryHat initialized (external device) - Bus: {Bus}, Address: 0x{Addr:X2}", _i2cBusId, _i2cAddress);
    }

    public int GetWatchdogPeriodSeconds()
    {
        try
        {
            lock (Sync)
            {
                return ReadUInt16(READ_INTERVAL_ADD);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetWatchdogPeriodSeconds failed");
            return -1;
        }
    }

    public int SetWatchdogPeriodSeconds(int seconds)
    {
        if (seconds < 1) seconds = 65001;
        try
        {
            lock (Sync)
            {
                WriteUInt16(WRITE_INTERVAL_ADD, (ushort)seconds);
            }
            _logger.LogDebug("SetWatchdogPeriodSeconds - Seconds: {Seconds}", seconds);
            return 1;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "SetWatchdogPeriodSeconds failed - Seconds: {Seconds}", seconds);
            return -1;
        }
    }

    public int ReloadWatchdog()
    {
        try
        {
            lock (Sync)
            {
                WriteByte(RELOAD_ADD, RELOAD_KEY);
            }
            _logger.LogInformation("ReloadWatchdog executed");
            return 1;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Reload failed");
            return -1;
        }
    }

    public int SetDefaultWatchdogPeriodSeconds(int seconds)
    {
        if (seconds <= 10 || seconds >= 65000)
            return -1;

        try
        {
            lock (Sync)
            {
                WriteUInt16(WRITE_INITIAL_INTERVAL_ADD, (ushort)seconds);
            }
            _logger.LogDebug("SetDefaultWatchdogPeriodSeconds - Seconds: {Seconds}", seconds);
            return 1;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "SetDefaultWatchdogPeriodSeconds failed - Seconds: {Seconds}", seconds);
            return -1;
        }
    }

    public int GetDefaultWatchdogPeriodSeconds()
    {
        try
        {
            lock (Sync)
            {
                return ReadUInt16(READ_INITIAL_INTERVAL_ADD);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetDefaultWatchdogPeriodSeconds failed");
            return -1;
        }
    }

    public int SetPowerOffIntervalSeconds(int seconds)
    {
        if (seconds <= 2 || seconds >= WDT_MAX_POWER_OFF_INTERVAL)
            return -1;

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
            return 1;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "SetPowerOffIntervalSeconds failed - Seconds: {Seconds}", seconds);
            return -1;
        }
    }

    public int GetPowerOffIntervalSeconds()
    {
        try
        {
            lock (Sync)
            {
                Span<byte> readBuffer = stackalloc byte[4];
                ReadBlock(POWER_OFF_INTERVAL_GET_ADD, readBuffer);
                return readBuffer[0] + (readBuffer[1] << 8) + (readBuffer[2] << 16) + (readBuffer[3] << 24);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetPowerOffIntervalSeconds failed");
            return -1;
        }
    }

    public int GetWatchdogResetCount()
    {
        try
        {
            lock (Sync)
            {
                return ReadUInt16(RESETS_COUNT_ADD);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetWatchdogResetCount failed");
            return -1;
        }
    }

    public double GetInputVoltageVin()
    {
        try
        {
            lock (Sync)
            {
                int val = ReadUInt16(V_IN_ADD);
                return val / 1000.0;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetInputVoltageVin failed");
            return -1;
        }
    }

    public double GetSystemVoltageVout()
    {
        try
        {
            lock (Sync)
            {
                int val = ReadUInt16(V_OUT_ADD);
                return val / 1000.0;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetSystemVoltageVout failed");
            return -1;
        }
    }

    public double GetBatteryVoltageVbat()
    {
        try
        {
            lock (Sync)
            {
                int val = ReadUInt16(V_BAT_ADD);
                return val / 1000.0;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetBatteryVoltageVbat failed");
            return -1;
        }
    }

    public int GetTemperatureCelsius()
    {
        try
        {
            lock (Sync)
            {
                return ReadByte(TEMP_ADD);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetTemperatureCelsius failed");
            return -1;
        }
    }

    public byte GetChargerStatusRaw()
    {
        try
        {
            lock (Sync)
            {
                return (byte)(ReadByte(CHARGE_STAT_ADD) & 0x0f);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetChargerStatusRaw failed");
            return 0xFF;
        }
    }

    public bool? GetRepowerOnBattery()
    {
        try
        {
            lock (Sync)
            {
                int stat = ReadByte(CHARGE_STAT_ADD) & 0xf0;
                if (stat > 0)
                {
                    int val = ReadByte(POWER_OFF_ON_BATTERY_ADD);
                    return val == 0;
                }
                return null;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetRepowerOnBattery failed");
            return null;
        }
    }

    public int SetRepowerOnBattery(bool enable)
    {
        try
        {
            lock (Sync)
            {
                int stat = ReadByte(CHARGE_STAT_ADD) & 0xf0;
                if (stat > 0)
                {
                    WriteByte(POWER_OFF_ON_BATTERY_ADD, enable ? (byte)0 : (byte)1);
                    return 1;
                }
                return -1;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "SetRepowerOnBattery failed - Enable: {Enable}", enable);
            return -1;
        }
    }

    public int GetPowerButtonEnableRaw()
    {
        try
        {
            lock (Sync)
            {
                int stat = ReadByte(CHARGE_STAT_ADD) & 0xf0;
                if (stat > 0x10)
                {
                    return ReadByte(POWER_SW_USAGE_ADD);
                }
                return 0;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetPowerButtonEnableRaw failed");
            return -1;
        }
    }

    public int SetPowerButtonEnable(bool enable)
    {
        try
        {
            lock (Sync)
            {
                int stat = ReadByte(CHARGE_STAT_ADD) & 0xf0;
                if (stat > 0x10)
                {
                    WriteByte(POWER_SW_USAGE_ADD, enable ? (byte)1 : (byte)0);
                    return 1;
                }
                return -1;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "SetPowerButtonEnable failed - Enable: {Enable}", enable);
            return -1;
        }
    }

    public int GetPowerButtonStatusRaw()
    {
        try
        {
            lock (Sync)
            {
                int stat = ReadByte(CHARGE_STAT_ADD) & 0xf0;
                if (stat > 0x10)
                {
                    return ReadByte(POWER_SW_STATUS_ADD);
                }
                return 0;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetPowerButtonStatusRaw failed");
            return -1;
        }
    }

    public int ClearPowerButtonStatus()
    {
        try
        {
            lock (Sync)
            {
                WriteByte(POWER_SW_STATUS_ADD, 0);
            }
            return 1;
        }
        catch (Exception)
        {
            return -1;
        }
    }

    public int GetPowerButtonStatus()
    {
        try
        {
            lock (Sync)
            {
                int stat = ReadByte(CHARGE_STAT_ADD) & 0xf0;
                if (stat > 0x10)
                {
                    return ReadByte(POWER_SW_STATUS_ADD);
                }
                return 0;
            }
        }
        catch (Exception)
        {
            return -1;
        }
    }

    public int SetPowerButtonStatus(int value)
    {
        value = Math.Clamp(value, 0, 1);
        try
        {
            lock (Sync)
            {
                WriteByte(POWER_SW_STATUS_ADD, (byte)value);
            }
            _logger.LogDebug("SetPowerButtonStatus - Value: {Value}", value);
            return 1;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "SetPowerButtonStatus failed - Value: {Value}", value);
            return -1;
        }
    }

    public int GetPowerButtonInterruptEnableRaw()
    {
        try
        {
            lock (Sync)
            {
                int stat = ReadByte(CHARGE_STAT_ADD) & 0xf0;
                if (stat > 0x10)
                {
                    return ReadByte(POWER_SW_INT_OUT_ADD);
                }
                return 0;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetPowerButtonInterruptEnableRaw failed");
            return -1;
        }
    }

    public int SetPowerButtonInterruptEnable(bool enable)
    {
        try
        {
            lock (Sync)
            {
                int stat = ReadByte(CHARGE_STAT_ADD) & 0xf0;
                if (stat > 0x10)
                {
                    WriteByte(POWER_SW_INT_OUT_ADD, enable ? (byte)1 : (byte)0);
                    return 1;
                }
                return -1;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "SetPowerButtonInterruptEnable failed - Enable: {Enable}", enable);
            return -1;
        }
    }

    public DateTime GetRtc()
    {
        try
        {
            lock (Sync)
            {
                Span<byte> buff = stackalloc byte[6];
                ReadBlock(I2C_RTC_YEAR_ADD, buff);
                return new DateTime(2000 + buff[0], buff[1], buff[2], buff[3], buff[4], buff[5], DateTimeKind.Local);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetRtc failed");
            throw new InvalidOperationException("Could not read RTC: " + ex.Message, ex);
        }
    }

    public void SetRtc(DateTime dateTime)
    {
        int y = dateTime.Year;
        int mo = dateTime.Month;
        int d = dateTime.Day;
        int h = dateTime.Hour;
        int m = dateTime.Minute;
        int s = dateTime.Second;
        if (y > 2000) y -= 2000;
        if (y < 0 || y > 255) throw new ArgumentOutOfRangeException(nameof(dateTime), "Invalid year in DateTime!");
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
            throw new InvalidOperationException("Could not set RTC: " + ex.Message, ex);
        }
    }

    // remove local I2C helpers; using base class helpers
}

#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
