namespace HVO.WebSite.RoofControllerV4.Logic
{
    public record RoofControllerOptions
    {
        public int RoofClosedLimitSwitchPin { get; set; } = 21;
        public int RoofOpenedLimitSwitchPin { get; set; } = 17;

        public TimeSpan LimitSwitchDebounce { get; set; } = TimeSpan.FromMilliseconds(50);

        public int CloseRoofRelayPin { get; set; } = 23; // FORWARD
        public int OpenRoofRelayPin { get; set; } = 24; // REVERSE
        public int StopRoofRelayPin { get; set; } = 25;
        public int KeypadEnableRelayPin { get; set; } = 26;

        public int CloseRoofButtonPin { get; set; } = 7;
        public int OpenRoofButtonPin { get; set; } = 8;
        public int StopRoofButtonPin { get; set; } = 9;

        public int? CloseRoofButtonLedPin { get; set; } = null; // Optional, if not set, no LED will be used
        public int? OpenRoofButtonLedPin { get; set; } = null; // Optional, if not set, no LED will be used
        public int? StopRoofButtonLedPin { get; set; } = null; // Optional, if not set, no LED will be used

        public TimeSpan ButtonDebounce { get; set; } = TimeSpan.FromMilliseconds(50);
    }
}