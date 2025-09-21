
using System;
using System.Device.I2c;

namespace HVO.CLI.RoofController
{
    public static class Wdt
    {
        // I2C Device constants
        private const int I2cBusId = 1;
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

        // RTC registers
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

        private static I2cDevice GetDevice()
        {
            var settings = new I2cConnectionSettings(I2cBusId, HW_ADD);
            return I2cDevice.Create(settings);
        }

        public static int GetPeriod()
        {
            using (var device = GetDevice())
            {
                try
                {
                    return ReadWord(device, READ_INTERVAL_ADD);
                }
                catch
                {
                    return -1;
                }
            }
        }

        public static int SetPeriod(int val)
        {
            if (val < 1) val = 65001;
            using (var device = GetDevice())
            {
                try
                {
                    WriteWord(device, WRITE_INTERVAL_ADD, (ushort)val);
                    return 1;
                }
                catch
                {
                    return -1;
                }
            }
        }

        public static int Reload()
        {
            using (var device = GetDevice())
            {
                try
                {
                    WriteByte(device, RELOAD_ADD, RELOAD_KEY);
                    return 1;
                }
                catch
                {
                    return -1;
                }
            }
        }

        public static int SetDefaultPeriod(int val)
        {
            if (val <= 10 || val >= 65000)
                return -1;

            using (var device = GetDevice())
            {
                try
                {
                    WriteWord(device, WRITE_INITIAL_INTERVAL_ADD, (ushort)val);
                    return 1;
                }
                catch
                {
                    return -1;
                }
            }
        }

        public static int GetDefaultPeriod()
        {
            using (var device = GetDevice())
            {
                try
                {
                    return ReadWord(device, READ_INITIAL_INTERVAL_ADD);
                }
                catch
                {
                    return -1;
                }
            }
        }

        public static int SetOffInterval(int val)
        {
            if (val <= 2 || val >= WDT_MAX_POWER_OFF_INTERVAL)
                return -1;

            byte[] buff = new byte[4];
            buff[0] = (byte)(val & 0xff);
            buff[1] = (byte)((val >> 8) & 0xff);
            buff[2] = (byte)((val >> 16) & 0xff);
            buff[3] = (byte)((val >> 24) & 0xff);

            using (var device = GetDevice())
            {
                try
                {
                    WriteBlock(device, POWER_OFF_INTERVAL_SET_ADD, buff);
                    return 1;
                }
                catch
                {
                    return -1;
                }
            }
        }

        public static int GetOffInterval()
        {
            using (var device = GetDevice())
            {
                try
                {
                    byte[] buff = ReadBlock(device, POWER_OFF_INTERVAL_GET_ADD, 4);
                    return buff[0] + (buff[1] << 8) + (buff[2] << 16) + (buff[3] << 24);
                }
                catch
                {
                    return -1;
                }
            }
        }

        public static int GetResetCount()
        {
            using (var device = GetDevice())
            {
                try
                {
                    return ReadWord(device, RESETS_COUNT_ADD);
                }
                catch
                {
                    return -1;
                }
            }
        }

        public static double GetVin()
        {
            using (var device = GetDevice())
            {
                try
                {
                    int val = ReadWord(device, V_IN_ADD);
                    return val / 1000.0;
                }
                catch
                {
                    return -1;
                }
            }
        }

        public static double GetVrasp()
        {
            using (var device = GetDevice())
            {
                try
                {
                    int val = ReadWord(device, V_OUT_ADD);
                    return val / 1000.0;
                }
                catch
                {
                    return -1;
                }
            }
        }

        public static double GetVbat()
        {
            using (var device = GetDevice())
            {
                try
                {
                    int val = ReadWord(device, V_BAT_ADD);
                    return val / 1000.0;
                }
                catch
                {
                    return -1;
                }
            }
        }

        public static int GetTemp()
        {
            using (var device = GetDevice())
            {
                try
                {
                    return ReadByte(device, TEMP_ADD);
                }
                catch
                {
                    return -1;
                }
            }
        }

        public static int GetChargeStat()
        {
            using (var device = GetDevice())
            {
                try
                {
                    return ReadByte(device, CHARGE_STAT_ADD) & 0x0f;
                }
                catch
                {
                    return -1;
                }
            }
        }

        public static int GetRepowerOnBattery()
        {
            using (var device = GetDevice())
            {
                try
                {
                    int stat = ReadByte(device, CHARGE_STAT_ADD) & 0xf0;
                    if (stat > 0)
                    {
                        int val = ReadByte(device, POWER_OFF_ON_BATTERY_ADD);
                        return val > 0 ? 0 : 1;
                    }
                    return -1;
                }
                catch
                {
                    return -1;
                }
            }
        }

        public static int SetRepowerOnBattery(int val)
        {
            using (var device = GetDevice())
            {
                try
                {
                    val = val != 0 ? 0 : 1;
                    int stat = ReadByte(device, CHARGE_STAT_ADD) & 0xf0;
                    if (stat > 0)
                    {
                        WriteByte(device, POWER_OFF_ON_BATTERY_ADD, (byte)val);
                        return 1;
                    }
                    return -1;
                }
                catch
                {
                    return -1;
                }
            }
        }

        public static int GetPowerButtonEnable()
        {
            using (var device = GetDevice())
            {
                try
                {
                    int stat = ReadByte(device, CHARGE_STAT_ADD) & 0xf0;
                    if (stat > 0x10)
                    {
                        return ReadByte(device, POWER_SW_USAGE_ADD);
                    }
                    return 0;
                }
                catch
                {
                    return -1;
                }
            }
        }

        public static int SetPowerButtonEnable(int val)
        {
            using (var device = GetDevice())
            {
                try
                {
                    val = val != 0 ? 1 : 0;
                    int stat = ReadByte(device, CHARGE_STAT_ADD) & 0xf0;
                    if (stat > 0x10)
                    {
                        WriteByte(device, POWER_SW_USAGE_ADD, (byte)val);
                        return 1;
                    }
                    return -1;
                }
                catch
                {
                    return -1;
                }
            }
        }

        public static int GetPowerButtonPush()
        {
            using (var device = GetDevice())
            {
                try
                {
                    int stat = ReadByte(device, CHARGE_STAT_ADD) & 0xf0;
                    if (stat > 0x10)
                    {
                        return ReadByte(device, POWER_SW_STATUS_ADD);
                    }
                    return 0;
                }
                catch
                {
                    return -1;
                }
            }
        }

        public static int ClrPowerButton()
        {
            using (var device = GetDevice())
            {
                try
                {
                    WriteByte(device, POWER_SW_STATUS_ADD, 0);
                    return 1;
                }
                catch
                {
                    return -1;
                }
            }
        }

        public static int GetPowerButton()
        {
            using (var device = GetDevice())
            {
                try
                {
                    int stat = ReadByte(device, CHARGE_STAT_ADD) & 0xf0;
                    if (stat > 0x10)
                    {
                        return ReadByte(device, POWER_SW_STATUS_ADD);
                    }
                    return 0;
                }
                catch
                {
                    return -1;
                }
            }
        }

        public static int SetPowerButton(int val)
        {
            val = Math.Clamp(val, 0, 1);
            using (var device = GetDevice())
            {
                try
                {
                    WriteByte(device, POWER_SW_STATUS_ADD, (byte)val);
                    return 1;
                }
                catch
                {
                    return -1;
                }
            }
        }

        public static int GetPowerButtonInterruptEnable()
        {
            using (var device = GetDevice())
            {
                try
                {
                    int stat = ReadByte(device, CHARGE_STAT_ADD) & 0xf0;
                    if (stat > 0x10)
                    {
                        return ReadByte(device, POWER_SW_INT_OUT_ADD);
                    }
                    return 0;
                }
                catch
                {
                    return -1;
                }
            }
        }

        public static int SetPowerButtonInterruptEnable(int val)
        {
            using (var device = GetDevice())
            {
                try
                {
                    val = val != 0 ? 1 : 0;
                    int stat = ReadByte(device, CHARGE_STAT_ADD) & 0xf0;
                    if (stat > 0x10)
                    {
                        WriteByte(device, POWER_SW_INT_OUT_ADD, (byte)val);
                        return 1;
                    }
                    return -1;
                }
                catch
                {
                    return -1;
                }
            }
        }

        public static (int year, int month, int day, int hour, int minute, int second) GetRTC()
        {
            using (var device = GetDevice())
            {
                try
                {
                    byte[] buff = ReadBlock(device, I2C_RTC_YEAR_ADD, 6);
                    return (2000 + buff[0], buff[1], buff[2], buff[3], buff[4], buff[5]);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Could not read RTC: " + ex.Message, ex);
                }
            }
        }

        public static void SetRTC(int y, int mo, int d, int h, int m, int s)
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

            byte[] buff = new byte[] { (byte)y, (byte)mo, (byte)d, (byte)h, (byte)m, (byte)s, 0xaa };

            using (var device = GetDevice())
            {
                try
                {
                    WriteBlock(device, I2C_RTC_SET_YEAR_ADD, buff);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Could not set RTC: " + ex.Message, ex);
                }
            }
        }

        // Helper I2C functions
        private static int ReadWord(I2cDevice device, byte reg)
        {
            Span<byte> writeBuffer = stackalloc byte[] { reg };
            Span<byte> readBuffer = stackalloc byte[2];
            device.WriteRead(writeBuffer, readBuffer);
            // Little-endian
            return readBuffer[0] | (readBuffer[1] << 8);
        }

        private static void WriteWord(I2cDevice device, byte reg, ushort value)
        {
            Span<byte> buffer = stackalloc byte[] { reg, (byte)(value & 0xff), (byte)((value >> 8) & 0xff) };
            device.Write(buffer);
        }

        private static int ReadByte(I2cDevice device, byte reg)
        {
            Span<byte> writeBuffer = stackalloc byte[] { reg };
            Span<byte> readBuffer = stackalloc byte[1];
            device.WriteRead(writeBuffer, readBuffer);
            return readBuffer[0];
        }

        private static void WriteByte(I2cDevice device, byte reg, byte value)
        {
            Span<byte> buffer = stackalloc byte[] { reg, value };
            device.Write(buffer);
        }

        private static byte[] ReadBlock(I2cDevice device, byte reg, int length)
        {
            Span<byte> writeBuffer = stackalloc byte[] { reg };
            byte[] readBuffer = new byte[length];
            device.WriteRead(writeBuffer, readBuffer);
            return readBuffer;
        }

        private static void WriteBlock(I2cDevice device, byte reg, byte[] data)
        {
            byte[] buffer = new byte[1 + data.Length];
            buffer[0] = reg;
            Array.Copy(data, 0, buffer, 1, data.Length);
            device.Write(buffer);
        }
    }
}