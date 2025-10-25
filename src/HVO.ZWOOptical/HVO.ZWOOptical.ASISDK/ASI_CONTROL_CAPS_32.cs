using System;
using System.Runtime.InteropServices;
using System.Text;

namespace HVO.ZWOOptical.ASISDK
{
public static partial class ASICamera2
{
        [StructLayout(LayoutKind.Sequential)]
    public struct ASI_CONTROL_CAPS_32
    {
        [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 64)]
        private byte[] name; //the name of the Control like Exposure, Gain etc..
        [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 128)]
        private byte[] description; //description of this control
        public int MaxValue; // 32bit - int
        public int MinValue; // 32bit - int
        public int DefaultValue; // 32bit - int
        public ASI_BOOL IsAutoSupported; //support auto set 1, don't support 0
        public ASI_BOOL IsWritable; //some control like temperature can only be read by some cameras 
        public ASI_CONTROL_TYPE ControlType;//this is used to get value and set value of the control
        [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 32)]
        public byte[] Unused;

        public string Name
        {
            get { return Encoding.ASCII.GetString(name).TrimEnd((Char)0); }
        }

        public string Description
        {
            get { return Encoding.ASCII.GetString(description).TrimEnd((Char)0); }
        }
    }


}
}
