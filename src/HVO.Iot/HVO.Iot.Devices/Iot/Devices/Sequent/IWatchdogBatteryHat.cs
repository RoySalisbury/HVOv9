using System;
using HVO;

namespace HVO.Iot.Devices.Iot.Devices.Sequent;

#pragma warning disable CS1591
/// <summary>
/// Abstraction for interacting with the Sequent Microsystems Watchdog/Battery HAT.
/// </summary>
public interface IWatchdogBatteryHat : IDisposable
{
    Result<int> GetWatchdogPeriodSeconds();
    Result<bool> SetWatchdogPeriodSeconds(int seconds);
    Result<bool> ReloadWatchdog();

    Result<bool> SetDefaultWatchdogPeriodSeconds(int seconds);
    Result<int> GetDefaultWatchdogPeriodSeconds();

    Result<bool> SetPowerOffIntervalSeconds(int seconds);
    Result<int> GetPowerOffIntervalSeconds();

    Result<int> GetWatchdogResetCount();

    Result<double> GetInputVoltageVin();
    Result<double> GetSystemVoltageVout();
    Result<double> GetBatteryVoltageVbat();

    Result<int> GetTemperatureCelsius();
    Result<byte> GetChargerStatusRaw();

    Result<bool?> GetRepowerOnBattery();
    Result<bool> SetRepowerOnBattery(bool enable);

    Result<int> GetPowerButtonEnableRaw();
    Result<bool> SetPowerButtonEnable(bool enable);

    Result<int> GetPowerButtonStatusRaw();
    Result<bool> ClearPowerButtonStatus();
    Result<int> GetPowerButtonStatus();
    Result<bool> SetPowerButtonStatus(int value);

    Result<int> GetPowerButtonInterruptEnableRaw();
    Result<bool> SetPowerButtonInterruptEnable(bool enable);

    Result<DateTime> GetRtc();
    Result<bool> SetRtc(DateTime dateTime);
}
#pragma warning restore CS1591
