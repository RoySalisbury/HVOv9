namespace HVO.ZWOOptical.ASISDK
{
public static partial class ASICamera2
{
        public enum ExposureStatus
    {
        ExpIdle = 0, //: idle states, you can start exposure now
        ExpWorking, //: exposing
        ExpSuccess, // exposure finished and waiting for download
        ExpFailed, //:exposure failed, you need to start exposure again
    }


}
}
