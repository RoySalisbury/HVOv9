using System;
using System.Runtime.InteropServices;

namespace HVO.ZWOOptical.ASISDK
{
public static partial class ASICamera2
{
        // Serial number (ASI_SN) per header: typedef ASI_ID { unsigned char id[8]; } ASI_SN;
        [StructLayout(LayoutKind.Sequential)]
    public struct ASI_SN
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] id;

        public override string ToString()
        {
            if (id == null) return string.Empty;
            // Represent as hex with no separators, typical for device IDs
            return BitConverter.ToString(id).Replace("-", string.Empty);
        }
    }


}
}
