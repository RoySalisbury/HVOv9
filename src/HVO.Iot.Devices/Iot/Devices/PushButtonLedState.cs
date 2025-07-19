namespace HVO.Iot.Devices;

/// <summary>
/// Represents the state of an LED in a push button device.
/// </summary>
public enum PushButtonLedState
{
    /// <summary>
    /// LED is turned off.
    /// </summary>
    Off = 0,
    
    /// <summary>
    /// LED is turned on.
    /// </summary>
    On = 1,
    
    /// <summary>
    /// LED is not used (no LED pin configured).
    /// </summary>
    NotUsed = 2
}