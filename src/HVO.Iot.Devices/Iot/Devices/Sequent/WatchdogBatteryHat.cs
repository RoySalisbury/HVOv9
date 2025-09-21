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

    public int GetPeriod()
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
            _logger.LogError(e, "GetPeriod failed");
            return -1;
        }
    }

    public int SetPeriod(int val)
    {
        if (val < 1) val = 65001;
        try
        {
            lock (Sync)
            {
                WriteUInt16(WRITE_INTERVAL_ADD, (ushort)val);
            }
            _logger.LogDebug("SetPeriod - Value: {Value}", val);
            return 1;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "SetPeriod failed - Value: {Value}", val);
            return -1;
        }
    }

    public int Reload()
    {
        try
        {
            lock (Sync)
            {
                WriteByte(RELOAD_ADD, RELOAD_KEY);
            }
            _logger.LogInformation("Watchdog reloaded");
            return 1;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Reload failed");
            return -1;
        }
    }

    public int SetDefaultPeriod(int val)
    {
        if (val <= 10 || val >= 65000)
            return -1;

        try
        {
            lock (Sync)
            {
                WriteUInt16(WRITE_INITIAL_INTERVAL_ADD, (ushort)val);
            }
            _logger.LogDebug("SetDefaultPeriod - Value: {Value}", val);
            return 1;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "SetDefaultPeriod failed - Value: {Value}", val);
            return -1;
        }
    }

    public int GetDefaultPeriod()
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
            _logger.LogError(e, "GetDefaultPeriod failed");
            return -1;
        }
    }

    public int SetOffInterval(int val)
    {
        if (val <= 2 || val >= WDT_MAX_POWER_OFF_INTERVAL)
            return -1;

        try
        {
            lock (Sync)
            {
                Span<byte> data = stackalloc byte[4];
                data[0] = (byte)(val & 0xff);
                data[1] = (byte)((val >> 8) & 0xff);
                data[2] = (byte)((val >> 16) & 0xff);
                data[3] = (byte)((val >> 24) & 0xff);
                WriteBlock(POWER_OFF_INTERVAL_SET_ADD, data);
            }
            _logger.LogDebug("SetOffInterval - Seconds: {Seconds}", val);
            return 1;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "SetOffInterval failed - Seconds: {Seconds}", val);
            return -1;
        }
    }

    public int GetOffInterval()
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
            _logger.LogError(e, "GetOffInterval failed");
            return -1;
        }
    }

    public int GetResetCount()
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
            _logger.LogError(e, "GetResetCount failed");
            return -1;
        }
    }

    public double GetVin()
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
            _logger.LogError(e, "GetVin failed");
            return -1;
        }
    }

    public double GetVrasp()
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
            _logger.LogError(e, "GetVrasp failed");
            return -1;
        }
    }

    public double GetVbat()
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
            _logger.LogError(e, "GetVbat failed");
            return -1;
        }
    }

    public int GetTemp()
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
            _logger.LogError(e, "GetTemp failed");
            return -1;
        }
    }

    public int GetChargeStat()
    {
        try
        {
            lock (Sync)
            {
                return ReadByte(CHARGE_STAT_ADD) & 0x0f;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetChargeStat failed");
            return -1;
        }
    }

    public int GetRepowerOnBattery()
    {
        try
        {
            lock (Sync)
            {
                int stat = ReadByte(CHARGE_STAT_ADD) & 0xf0;
                if (stat > 0)
                {
                    int val = ReadByte(POWER_OFF_ON_BATTERY_ADD);
                    return val > 0 ? 0 : 1;
                }
                return -1;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "GetRepowerOnBattery failed");
            return -1;
        }
    }

    public int SetRepowerOnBattery(int val)
    {
        try
        {
            lock (Sync)
            {
                val = val != 0 ? 0 : 1;
                int stat = ReadByte(CHARGE_STAT_ADD) & 0xf0;
                if (stat > 0)
                {
                    WriteByte(POWER_OFF_ON_BATTERY_ADD, (byte)val);
                    return 1;
                }
                return -1;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "SetRepowerOnBattery failed - Value: {Value}", val);
            return -1;
        }
    }

    public int GetPowerButtonEnable()
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
            _logger.LogError(e, "GetPowerButtonEnable failed");
            return -1;
        }
    }

    public int SetPowerButtonEnable(int val)
    {
        try
        {
            lock (Sync)
            {
                val = val != 0 ? 1 : 0;
                int stat = ReadByte(CHARGE_STAT_ADD) & 0xf0;
                if (stat > 0x10)
                {
                    WriteByte(POWER_SW_USAGE_ADD, (byte)val);
                    return 1;
                }
                return -1;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "SetPowerButtonEnable failed - Value: {Value}", val);
            return -1;
        }
    }

    public int GetPowerButtonPush()
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
            _logger.LogError(e, "GetPowerButtonPush failed");
            return -1;
        }
    }

    public int ClrPowerButton()
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

    public int GetPowerButton()
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

    public int SetPowerButton(int val)
    {
        val = Math.Clamp(val, 0, 1);
        try
        {
            lock (Sync)
            {
                WriteByte(POWER_SW_STATUS_ADD, (byte)val);
            }
            _logger.LogDebug("SetPowerButton - Value: {Value}", val);
            return 1;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "SetPowerButton failed - Value: {Value}", val);
            return -1;
        }
    }

    public int GetPowerButtonInterruptEnable()
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
            _logger.LogError(e, "GetPowerButtonInterruptEnable failed");
            return -1;
        }
    }

    public int SetPowerButtonInterruptEnable(int val)
    {
        try
        {
            lock (Sync)
            {
                val = val != 0 ? 1 : 0;
                int stat = ReadByte(CHARGE_STAT_ADD) & 0xf0;
                if (stat > 0x10)
                {
                    WriteByte(POWER_SW_INT_OUT_ADD, (byte)val);
                    return 1;
                }
                return -1;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "SetPowerButtonInterruptEnable failed - Value: {Value}", val);
            return -1;
        }
    }

    public (int year, int month, int day, int hour, int minute, int second) GetRTC()
    {
        try
        {
            lock (Sync)
            {
                Span<byte> buff = stackalloc byte[6];
                ReadBlock(I2C_RTC_YEAR_ADD, buff);
                return (2000 + buff[0], buff[1], buff[2], buff[3], buff[4], buff[5]);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetRTC failed");
            throw new InvalidOperationException("Could not read RTC: " + ex.Message, ex);
        }
    }

    public void SetRTC(int y, int mo, int d, int h, int m, int s)
    {
        if (y > 2000) y -= 2000;
        if (y < 0 || y > 255)
            throw new ArgumentOutOfRangeException(nameof(y), "Invalid year!");
        if (mo < 1 || mo > 12)
            throw new ArgumentOutOfRangeException(nameof(mo), "Invalid month!");
        if (d < 1 || d > 31)
            throw new ArgumentOutOfRangeException(nameof(d), "Invalid day!");
        if (h < 0 || h > 23)
            throw new ArgumentOutOfRangeException(nameof(h), "Invalid hour!");
        if (m < 0 || m > 59)
            throw new ArgumentOutOfRangeException(nameof(m), "Invalid minute!");
        if (s < 0 || s > 59)
            throw new ArgumentOutOfRangeException(nameof(s), "Invalid second!");

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
            _logger.LogError(ex, "SetRTC failed");
            throw new InvalidOperationException("Could not set RTC: " + ex.Message, ex);
        }
    }

    // remove local I2C helpers; using base class helpers
}

#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
