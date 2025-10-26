using System;
using System.Runtime.InteropServices;
using System.Text;

namespace HVO.ZWOOptical.ASISDK
{
public static partial class ASICamera2
{
        [StructLayout(LayoutKind.Sequential)]
    public struct ASI_CAMERA_INFO_32
    {
        [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 64)]
        private byte[] name; //the name of the camera, you can display this to the UI
        public int CameraID; //this is used to control everything of the camera in other functions
        public int MaxHeight; //the max height of the camera // 32bit - int
        public int MaxWidth; //the max width of the camera // 32bit - int

        public ASI_BOOL IsColorCam;
        public ASI_BAYER_PATTERN BayerPattern;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public int[] SupportedBins; //1 means bin1 which is supported by every camera, 2 means bin 2 etc.. 0 is the end of supported binning method
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public ASI_IMG_TYPE[] SupportedVideoFormat;// ASI_IMG_TYPE[8]; //this array will content with the support output format type.IMG_END is the end of supported video format

        public double PixelSize; //the pixel size of the camera, unit is um. such like 5.6um
        public ASI_BOOL MechanicalShutter;
        public ASI_BOOL ST4Port;
        public ASI_BOOL IsCoolerCam;
        public ASI_BOOL IsUSB3Host;
        public ASI_BOOL IsUSB3Camera;
        public float ElecPerADU;
        public int BitDepth;
        public ASI_BOOL IsTriggerCam;

        [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 16)]
        public byte[] Unused;

        public string Name
        {
            get { return Encoding.ASCII.GetString(name).TrimEnd((Char)0); }
        }
    };


}
}
