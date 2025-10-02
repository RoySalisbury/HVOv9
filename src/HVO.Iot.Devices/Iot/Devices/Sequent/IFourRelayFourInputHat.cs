using System;
using HVO;

namespace HVO.Iot.Devices.Iot.Devices.Sequent;

#pragma warning disable CS1591
/// <summary>
/// Abstraction for interacting with the Sequent Microsystems Four Relay / Four Input HAT.
/// Enables dependency injection to swap between hardware-backed and simulated implementations.
/// </summary>
public interface IFourRelayFourInputHat : IDisposable
{
    TimeSpan DigitalInputPollInterval { get; set; }

    event EventHandler<bool>? DigitalInput1Changed;
    event EventHandler<bool>? DigitalInput2Changed;
    event EventHandler<bool>? DigitalInput3Changed;
    event EventHandler<bool>? DigitalInput4Changed;

    (byte Major, byte Minor) HardwareRevision { get; }
    (byte Major, byte Minor) SoftwareRevision { get; }

    Result<bool> SetRelay(int relayIndex, bool isOn);
    Result<bool> TrySetRelayWithRetry(int relayIndex, bool desiredState, int attempts = 3, int delayMs = 5);
    Result<bool> SetRelaysMask(byte mask);
    Result<bool> IsRelayOn(int relayIndex);
    Result<byte> GetRelaysMask();

    Result<bool> IsDigitalInputHigh(int inputIndex);
    Result<byte> GetDigitalInputsMask();
    Result<(bool in1, bool in2, bool in3, bool in4)> GetAllDigitalInputs();

    Result<bool> IsAcInputActive(int inputIndex);
    Result<byte> GetAcInputsMask();
    Result<(bool in1, bool in2, bool in3, bool in4)> GetAllAcInputs();

    Result<bool> IsCounterEnabled(int inputIndex);
    Result<bool> SetCounterEnabled(int inputIndex, bool enabled);
    Result<uint> GetPulseCount(int inputIndex);
    Result<bool> ResetPulseCount(int inputIndex);
    Result<int> GetPulsesPerSecond(int inputIndex);

    Result<bool> IsEncoderEnabled(int encoderIndex);
    Result<bool> SetEncoderEnabled(int encoderIndex, bool enabled);
    Result<int> GetEncoderCount(int encoderIndex);
    Result<bool> ResetEncoderCount(int encoderIndex);

    Result<double> GetInputFrequencyHz(int inputIndex);
    Result<double> GetPwmDutyCyclePercent(int inputIndex);
    Result<double> GetCurrentAmps(int relayIndex);
    Result<double> GetCurrentRmsAmps(int relayIndex);

    Result<bool> SetLedsMask(byte mask);
    Result<bool> SetAllLeds(bool led1, bool led2, bool led3, bool led4);
    Result<byte> GetLedsMask();
    Result<(bool led1, bool led2, bool led3, bool led4)> GetAllLeds();

    Result<bool> SetLedMode(int ledIndex, FourRelayFourInputHat.LedMode mode);
    Result<FourRelayFourInputHat.LedMode> GetLedMode(int ledIndex);
    Result<byte> GetLedModesMask();
    Result<(FourRelayFourInputHat.LedMode led1, FourRelayFourInputHat.LedMode led2, FourRelayFourInputHat.LedMode led3, FourRelayFourInputHat.LedMode led4)> GetAllLedModes();
    Result<bool> SetLedModesMask(byte mask);
    Result<bool> SetAllLedModes(FourRelayFourInputHat.LedMode led1, FourRelayFourInputHat.LedMode led2, FourRelayFourInputHat.LedMode led3, FourRelayFourInputHat.LedMode led4);
    Result<bool> SetLed(int ledIndex, bool isOn);
    Result<bool> IsLedOn(int ledIndex);
}
#pragma warning restore CS1591
