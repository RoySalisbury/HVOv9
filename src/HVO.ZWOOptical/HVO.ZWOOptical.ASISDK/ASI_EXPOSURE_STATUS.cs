namespace HVO.ZWOOptical.ASISDK
{
public static partial class ASICamera2
{
        public enum ASI_EXPOSURE_STATUS
    {
        ASI_EXP_IDLE = 0,//: idle states, you can start exposure now
        ASI_EXP_WORKING,//: exposing
        ASI_EXP_SUCCESS,// exposure finished and waiting for download
        ASI_EXP_FAILED,//:exposure failed, you need to start exposure again
    };


}
}
