namespace HVO.ZWOOptical.ASISDK
{
public static partial class ASICamera2
{
        public enum ASI_CONTROL_TYPE
    {
        ASI_GAIN = 0,
        ASI_EXPOSURE,
        ASI_GAMMA,
        ASI_WB_R,
        ASI_WB_B,
        ASI_BRIGHTNESS,
        ASI_BANDWIDTHOVERLOAD,
        ASI_OVERCLOCK,
        ASI_TEMPERATURE,
        ASI_FLIP,
        ASI_AUTO_MAX_GAIN,
        ASI_AUTO_MAX_EXP,
        ASI_AUTO_MAX_BRIGHTNESS,
        ASI_HARDWARE_BIN,
        ASI_HIGH_SPEED_MODE,
        ASI_COOLER_POWER_PERC,
        ASI_TARGET_TEMP,
        ASI_COOLER_ON,
        ASI_MONO_BIN,
        ASI_FAN_ON,
        ASI_PATTERN_ADJUST,
        ASI_ANTI_DEW_HEATER,
        ASI_FAN_ADJUST,
        ASI_PWRLED_BRIGNT,
        ASI_USBHUB_RESET,
        ASI_GPS_SUPPORT,
        ASI_GPS_START_LINE,
        ASI_GPS_END_LINE,
        ASI_ROLLING_INTERVAL,
        // Aliases for newer SDK names (macros in C header)
        ASI_OFFSET = ASI_BRIGHTNESS,
        ASI_AUTO_TARGET_BRIGHTNESS = ASI_AUTO_MAX_BRIGHTNESS
    }


}
}
