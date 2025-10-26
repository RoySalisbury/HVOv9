namespace HVO.Iot.Devices;

/// <summary>
/// Defines the behavior options for LED control in push button devices.
/// </summary>
public enum PushButtonLedOptions
{
    /// <summary>
    /// LED is always off regardless of button state.
    /// </summary>
    AlwaysOff,
    
    /// <summary>
    /// LED is always on regardless of button state.
    /// </summary>
    AlwaysOn,
    
    /// <summary>
    /// LED state follows the button pressed state.
    /// </summary>
    FollowPressedState
}
