using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Linq;

namespace HVO.ZWOOptical.ASISDK
{
public static partial class ASICamera2
{
    private const string ASILIBRARY = "ASICamera2";

    // Determines if the native SDK uses 64-bit C 'long' (Linux/macOS) vs 32-bit (Windows)
    public static bool Use64Structs { get; } =
        System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
        System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    [DllImport(ASILIBRARY, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ASIGetNumOfConnectedCameras")]
    public static extern int GetNumOfConnectedCameras();


    [DllImport(ASILIBRARY, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ASIGetCameraProperty")]
    private static extern ASI_ERROR_CODE ASIGetCameraProperty32(out ASI_CAMERA_INFO_32 pASICameraInfo, int iCameraIndex);

    [DllImport(ASILIBRARY, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ASIGetCameraProperty")]
    private static extern ASI_ERROR_CODE ASIGetCameraProperty64(out ASI_CAMERA_INFO_64 pASICameraInfo, int iCameraIndex);

    public static ASI_CAMERA_INFO_32 GetCameraProperties32(int cameraIndex)
    {
        ASI_CAMERA_INFO_32 result;
        CheckReturn(ASIGetCameraProperty32(out result, cameraIndex), MethodBase.GetCurrentMethod(), cameraIndex);
        return result;
    }
    public static ASI_CAMERA_INFO_64 GetCameraProperties64(int cameraIndex)
    {
        ASI_CAMERA_INFO_64 result;
        CheckReturn(ASIGetCameraProperty64(out result, cameraIndex), MethodBase.GetCurrentMethod(), cameraIndex);
        return result;
    }


    // Unified compat method returning a 32-bit shaped struct regardless of platform
    public static ASI_CAMERA_INFO_32 GetCameraPropertiesCompat(int cameraIndex)
        {
            if (!Use64Structs)
            {
                return GetCameraProperties32(cameraIndex);
            }

            var cam64 = GetCameraProperties64(cameraIndex);
            var cam32 = new ASI_CAMERA_INFO_32
            {
                CameraID = cam64.CameraID,
                MaxHeight = (int)Math.Clamp(cam64.MaxHeight, int.MinValue, int.MaxValue),
                MaxWidth = (int)Math.Clamp(cam64.MaxWidth, int.MinValue, int.MaxValue),
                IsColorCam = cam64.IsColorCam,
                BayerPattern = cam64.BayerPattern,
                SupportedBins = cam64.SupportedBins,
                SupportedVideoFormat = cam64.SupportedVideoFormat,
                PixelSize = cam64.PixelSize,
                MechanicalShutter = cam64.MechanicalShutter,
                ST4Port = cam64.ST4Port,
                IsCoolerCam = cam64.IsCoolerCam,
                IsUSB3Host = cam64.IsUSB3Host,
                IsUSB3Camera = cam64.IsUSB3Camera,
                ElecPerADU = cam64.ElecPerADU,
                BitDepth = cam64.BitDepth,
                IsTriggerCam = cam64.IsTriggerCam,
                Unused = cam64.Unused
            };

            // Copy private name field
            try
            {
                var nameField64 = typeof(ASI_CAMERA_INFO_64).GetField("name", BindingFlags.NonPublic | BindingFlags.Instance);
                var nameField32 = typeof(ASI_CAMERA_INFO_32).GetField("name", BindingFlags.NonPublic | BindingFlags.Instance);
                if (nameField64 != null && nameField32 != null)
                {
                    var nameVal = nameField64.GetValue(cam64);
                    nameField32.SetValueDirect(__makeref(cam32), nameVal);
                }
            }
            catch { }

            return cam32;
        }

    private static void CheckReturn(ASI_ERROR_CODE errorCode, MethodBase callingMethod, params object[] parameters)
    {
        switch (errorCode)
        {
            case ASI_ERROR_CODE.ASI_SUCCESS:
                break;
            case ASI_ERROR_CODE.ASI_ERROR_INVALID_INDEX:
            case ASI_ERROR_CODE.ASI_ERROR_INVALID_ID:
            case ASI_ERROR_CODE.ASI_ERROR_INVALID_CONTROL_TYPE:
            case ASI_ERROR_CODE.ASI_ERROR_CAMERA_CLOSED:
            case ASI_ERROR_CODE.ASI_ERROR_CAMERA_REMOVED:
            case ASI_ERROR_CODE.ASI_ERROR_INVALID_PATH:
            case ASI_ERROR_CODE.ASI_ERROR_INVALID_FILEFORMAT:
            case ASI_ERROR_CODE.ASI_ERROR_INVALID_SIZE:
            case ASI_ERROR_CODE.ASI_ERROR_INVALID_IMGTYPE:
            case ASI_ERROR_CODE.ASI_ERROR_OUTOF_BOUNDARY:
            case ASI_ERROR_CODE.ASI_ERROR_TIMEOUT:
            case ASI_ERROR_CODE.ASI_ERROR_INVALID_SEQUENCE:
            case ASI_ERROR_CODE.ASI_ERROR_BUFFER_TOO_SMALL:
            case ASI_ERROR_CODE.ASI_ERROR_VIDEO_MODE_ACTIVE:
            case ASI_ERROR_CODE.ASI_ERROR_EXPOSURE_IN_PROGRESS:
            case ASI_ERROR_CODE.ASI_ERROR_GENERAL_ERROR:
            case ASI_ERROR_CODE.ASI_ERROR_GPS_NOT_SUPPORTED:
            case ASI_ERROR_CODE.ASI_ERROR_GPS_VER_ERR:
            case ASI_ERROR_CODE.ASI_ERROR_GPS_FPGA_ERR:
            case ASI_ERROR_CODE.ASI_ERROR_GPS_PARAM_OUT_OF_RANGE:
            case ASI_ERROR_CODE.ASI_ERROR_GPS_DATA_INVALID:
            case ASI_ERROR_CODE.ASI_ERROR_END:
                throw new ASICameraException(errorCode, callingMethod, parameters);
            default:
                throw new ArgumentOutOfRangeException("errorCode");
        }
    }






    [DllImport(ASILIBRARY, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ASIOpenCamera")]
    private static extern ASI_ERROR_CODE ASIOpenCamera(int iCameraID);

    public static void OpenCamera(int cameraId)
    {
        CheckReturn(ASIOpenCamera(cameraId), MethodBase.GetCurrentMethod(), cameraId);
    }

    [DllImport(ASILIBRARY, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ASIInitCamera")]
    private static extern ASI_ERROR_CODE ASIInitCamera(int iCameraID);

    public static void InitCamera(int cameraId)
    {
        CheckReturn(ASIInitCamera(cameraId), MethodBase.GetCurrentMethod(), cameraId);
    }



    [DllImport(ASILIBRARY, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ASICloseCamera")]
    private static extern ASI_ERROR_CODE ASICloseCamera(int iCameraID);

    public static void CloseCamera(int cameraId)
    {
        CheckReturn(ASICloseCamera(cameraId), MethodBase.GetCurrentMethod(), cameraId);
    }



    [DllImport(ASILIBRARY, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ASIGetNumOfControls")]
    private static extern ASI_ERROR_CODE ASIGetNumOfControls(int iCameraID, out int piNumberOfControls);

    public static int GetNumOfControls(int cameraId)
    {
        int result;
        CheckReturn(ASIGetNumOfControls(cameraId, out result), MethodBase.GetCurrentMethod(), cameraId);
        return result;
    }


    [DllImport(ASILIBRARY, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ASIGetControlCaps")]
    private static extern ASI_ERROR_CODE ASIGetControlCaps32(int iCameraID, int iControlIndex, out ASI_CONTROL_CAPS_32 pControlCaps);

    [DllImport(ASILIBRARY, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ASIGetControlCaps")]
    private static extern ASI_ERROR_CODE ASIGetControlCaps64(int iCameraID, int iControlIndex, out ASI_CONTROL_CAPS_64 pControlCaps);


    public static ASI_CONTROL_CAPS_32 GetControlCaps32(int cameraIndex, int controlIndex)
    {
        ASI_CONTROL_CAPS_32 result;
        CheckReturn(ASIGetControlCaps32(cameraIndex, controlIndex, out result), MethodBase.GetCurrentMethod(), cameraIndex, controlIndex);
        return result;
    }

    public static ASI_CONTROL_CAPS_64 GetControlCaps64(int cameraIndex, int controlIndex)
    {
        ASI_CONTROL_CAPS_64 result;
        CheckReturn(ASIGetControlCaps64(cameraIndex, controlIndex, out result), MethodBase.GetCurrentMethod(), cameraIndex, controlIndex);
        return result;
    }


    [DllImport(ASILIBRARY, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ASIGetControlValue")]
    private static extern ASI_ERROR_CODE ASIGetControlValue32(int iCameraID, ASI_CONTROL_TYPE controlType, out int plValue, out ASI_BOOL pbAuto);

    [DllImport(ASILIBRARY, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ASIGetControlValue")]
    private static extern ASI_ERROR_CODE ASIGetControlValue64(int iCameraID, ASI_CONTROL_TYPE controlType, out long plValue, out ASI_BOOL pbAuto);


    public static int GetControlValue32(int cameraId, ASI_CONTROL_TYPE controlType, out bool isAuto)
    {
        ASI_BOOL auto;
        int result;  // 32bit - int
        CheckReturn(ASIGetControlValue32(cameraId, controlType, out result, out auto), MethodBase.GetCurrentMethod(), cameraId, controlType);
        isAuto = auto != ASI_BOOL.ASI_FALSE;
        return result;
    }

    public static long GetControlValue64(int cameraId, ASI_CONTROL_TYPE controlType, out bool isAuto)
    {
        ASI_BOOL auto;
        long result;
        CheckReturn(ASIGetControlValue64(cameraId, controlType, out result, out auto), MethodBase.GetCurrentMethod(), cameraId, controlType);
        isAuto = auto != ASI_BOOL.ASI_FALSE;
        return result;
    }


    [DllImport(ASILIBRARY, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ASISetControlValue")]
    private static extern ASI_ERROR_CODE ASISetControlValue32(int iCameraID, ASI_CONTROL_TYPE controlType, int lValue, ASI_BOOL bAuto);

    [DllImport(ASILIBRARY, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ASISetControlValue")]
    private static extern ASI_ERROR_CODE ASISetControlValue64(int iCameraID, ASI_CONTROL_TYPE controlType, long lValue, ASI_BOOL bAuto);


    public static void SetControlValue32(int cameraId, ASI_CONTROL_TYPE controlType, int value, bool auto)
    {
        CheckReturn(ASISetControlValue32(cameraId, controlType, value, auto ? ASI_BOOL.ASI_TRUE : ASI_BOOL.ASI_FALSE), MethodBase.GetCurrentMethod(), cameraId, controlType, value, auto);
    }

    public static void SetControlValue64(int cameraId, ASI_CONTROL_TYPE controlType, long value, bool auto)
    {
        CheckReturn(ASISetControlValue64(cameraId, controlType, value, auto ? ASI_BOOL.ASI_TRUE : ASI_BOOL.ASI_FALSE), MethodBase.GetCurrentMethod(), cameraId, controlType, value, auto);
    }

    // ---------------------------------------------------------------------
    // Compat helper methods (single call sites pick correct 32/64 implementation)
    // ---------------------------------------------------------------------
    public static ASI_CONTROL_CAPS_32 GetControlCapsCompat(int cameraId, int controlIndex)
    {
        if (!Use64Structs)
        {
            return GetControlCaps32(cameraId, controlIndex);
        }

        var caps64 = GetControlCaps64(cameraId, controlIndex);
        // Down-convert long fields to int with clamping (most control ranges fit in int)
        var result = new ASI_CONTROL_CAPS_32
        {
            MaxValue = (int)Math.Clamp(caps64.MaxValue, int.MinValue, int.MaxValue),
            MinValue = (int)Math.Clamp(caps64.MinValue, int.MinValue, int.MaxValue),
            DefaultValue = (int)Math.Clamp(caps64.DefaultValue, int.MinValue, int.MaxValue),
            IsAutoSupported = caps64.IsAutoSupported,
            IsWritable = caps64.IsWritable,
            ControlType = caps64.ControlType,
            Unused = caps64.Unused
        };

        // Copy name/description via reflection because fields are private in the struct
        try
        {
            var nameField32 = typeof(ASI_CONTROL_CAPS_32).GetField("name", BindingFlags.NonPublic | BindingFlags.Instance);
            var nameField64 = typeof(ASI_CONTROL_CAPS_64).GetField("name", BindingFlags.NonPublic | BindingFlags.Instance);
            var descField32 = typeof(ASI_CONTROL_CAPS_32).GetField("description", BindingFlags.NonPublic | BindingFlags.Instance);
            var descField64 = typeof(ASI_CONTROL_CAPS_64).GetField("description", BindingFlags.NonPublic | BindingFlags.Instance);
            if (nameField32 != null && nameField64 != null)
            {
                var nameValue = nameField64.GetValue(caps64);
                nameField32.SetValueDirect(__makeref(result), nameValue);
            }
            if (descField32 != null && descField64 != null)
            {
                var descValue = descField64.GetValue(caps64);
                descField32.SetValueDirect(__makeref(result), descValue);
            }
        }
        catch { /* non-fatal; name/description may appear blank if reflection fails */ }

        return result;
    }

    public static int GetControlValueCompat(int cameraId, ASI_CONTROL_TYPE controlType, out bool isAuto)
    {
        if (!Use64Structs)
        {
            return GetControlValue32(cameraId, controlType, out isAuto);
        }
        var v64 = GetControlValue64(cameraId, controlType, out isAuto);
        return (int)Math.Clamp(v64, int.MinValue, int.MaxValue);
    }

    public static void SetControlValueCompat(int cameraId, ASI_CONTROL_TYPE controlType, int value, bool auto)
    {
        if (!Use64Structs)
        {
            SetControlValue32(cameraId, controlType, value, auto);
        }
        else
        {
            SetControlValue64(cameraId, controlType, value, auto);
        }
    }


    [DllImport(ASILIBRARY, CallingConvention = CallingConvention.Cdecl)]
    private static extern ASI_ERROR_CODE ASISetROIFormat(int iCameraID, int iWidth, int iHeight, int iBin, ASI_IMG_TYPE Img_type);

    public static void SetROIFormat(int cameraId, Size size, int bin, ASI_IMG_TYPE imageType)
    {
        CheckReturn(ASISetROIFormat(cameraId, size.Width, size.Height, bin, imageType), MethodBase.GetCurrentMethod(), cameraId, size, bin, imageType);
    }


    [DllImport(ASILIBRARY, CallingConvention = CallingConvention.Cdecl)]
    private static extern ASI_ERROR_CODE ASIGetROIFormat(int iCameraID, out int piWidth, out int piHeight, out int piBin, out ASI_IMG_TYPE pImg_type);

    public static Size GetROIFormat(int cameraId, out int bin, out ASI_IMG_TYPE imageType)
    {
        int width, height;
        CheckReturn(ASIGetROIFormat(cameraId, out width, out height, out bin, out imageType), MethodBase.GetCurrentMethod(), cameraId, bin);
        return new Size(width, height);
    }

    [DllImport(ASILIBRARY, CallingConvention = CallingConvention.Cdecl)]
    private static extern ASI_ERROR_CODE ASISetStartPos(int iCameraID, int iStartX, int iStartY);

    public static void SetStartPos(int cameraId, Point startPos)
    {
        CheckReturn(ASISetStartPos(cameraId, startPos.X, startPos.Y), MethodBase.GetCurrentMethod(), cameraId, startPos);
    }


    [DllImport(ASILIBRARY, CallingConvention = CallingConvention.Cdecl)]
    private static extern ASI_ERROR_CODE ASIGetStartPos(int iCameraID, out int piStartX, out int piStartY);

    public static Point GetStartPos(int cameraId)
    {
        int x, y;
        CheckReturn(ASIGetStartPos(cameraId, out x, out y), MethodBase.GetCurrentMethod(), cameraId);
        return new Point(x, y);
    }


    [DllImport(ASILIBRARY, CallingConvention = CallingConvention.Cdecl)]
    private static extern ASI_ERROR_CODE ASIGetDroppedFrames(int iCameraID, out int piDropFrames);

    public static int GetDroppedFrames(int cameraId)
    {
        int result;
        CheckReturn(ASIGetDroppedFrames(cameraId, out result), MethodBase.GetCurrentMethod(), cameraId);
        return result;
    }


    [DllImport(ASILIBRARY, CallingConvention = CallingConvention.Cdecl)]
    private static extern ASI_ERROR_CODE ASIEnableDarkSubtract(int iCameraID, [MarshalAs(UnmanagedType.LPStr)] string pcBMPPath, out ASI_BOOL bIsSubDarkWorking);

    public static bool EnableDarkSubtract(int cameraId, string darkFilePath)
    {
        ASI_BOOL result;
        CheckReturn(ASIEnableDarkSubtract(cameraId, darkFilePath, out result), MethodBase.GetCurrentMethod(), cameraId, darkFilePath);
        return result != ASI_BOOL.ASI_FALSE;
    }


    [DllImport(ASILIBRARY, CallingConvention = CallingConvention.Cdecl)]
    private static extern ASI_ERROR_CODE ASIDisableDarkSubtract(int iCameraID);

    public static void DisableDarkSubtract(int cameraId)
    {
        CheckReturn(ASIDisableDarkSubtract(cameraId), MethodBase.GetCurrentMethod(), cameraId);
    }


    [DllImport(ASILIBRARY, CallingConvention = CallingConvention.Cdecl)]
    private static extern ASI_ERROR_CODE ASIStartVideoCapture(int iCameraID);

    public static void StartVideoCapture(int cameraId)
    {
        CheckReturn(ASIStartVideoCapture(cameraId), MethodBase.GetCurrentMethod(), cameraId);
    }


    [DllImport(ASILIBRARY, CallingConvention = CallingConvention.Cdecl)]
    private static extern ASI_ERROR_CODE ASIStopVideoCapture(int iCameraID);

    public static void StopVideoCapture(int cameraId)
    {
        CheckReturn(ASIStopVideoCapture(cameraId), MethodBase.GetCurrentMethod(), cameraId);
    }

    [DllImport(ASILIBRARY, CallingConvention = CallingConvention.Cdecl)]
    private static extern ASI_ERROR_CODE ASIGetVideoData(int iCameraID, IntPtr pBuffer, long lBuffSize, int iWaitms);

    public static bool GetVideoData(int cameraId, IntPtr buffer, long bufferSize, int waitMs)
    {
        var result = ASIGetVideoData(cameraId, buffer, bufferSize, waitMs);

        if (result == ASI_ERROR_CODE.ASI_ERROR_TIMEOUT)
            return false;

        //CheckReturn(result, MethodBase.GetCurrentMethod(), cameraId, buffer, bufferSize, waitMs);
        return true;
    }


    [DllImport(ASILIBRARY, CallingConvention = CallingConvention.Cdecl)]
    private static extern ASI_ERROR_CODE ASIPulseGuideOn(int iCameraID, ASI_GUIDE_DIRECTION direction);

    public static void PulseGuideOn(int cameraId, ASI_GUIDE_DIRECTION direction)
    {
        CheckReturn(ASIPulseGuideOn(cameraId, direction), MethodBase.GetCurrentMethod(), cameraId, direction);
    }


    [DllImport(ASILIBRARY, CallingConvention = CallingConvention.Cdecl)]
    private static extern ASI_ERROR_CODE ASIPulseGuideOff(int iCameraID, ASI_GUIDE_DIRECTION direction);

    public static void PulseGuideOff(int cameraId, ASI_GUIDE_DIRECTION direction)
    {
        CheckReturn(ASIPulseGuideOff(cameraId, direction), MethodBase.GetCurrentMethod(), cameraId, direction);
    }



    [DllImport(ASILIBRARY, CallingConvention = CallingConvention.Cdecl)]
    private static extern ASI_ERROR_CODE ASIStartExposure(int iCameraID, ASI_BOOL bIsDark);

    public static void StartExposure(int cameraId, bool isDark)
    {
        CheckReturn(ASIStartExposure(cameraId, isDark ? ASI_BOOL.ASI_TRUE : ASI_BOOL.ASI_FALSE), MethodBase.GetCurrentMethod(), cameraId, isDark);
    }


    [DllImport(ASILIBRARY, CallingConvention = CallingConvention.Cdecl)]
    private static extern ASI_ERROR_CODE ASIStopExposure(int iCameraID);

    public static void StopExposure(int cameraId)
    {
        CheckReturn(ASIStopExposure(cameraId), MethodBase.GetCurrentMethod(), cameraId);
    }


    [DllImport(ASILIBRARY, CallingConvention = CallingConvention.Cdecl)]
    private static extern ASI_ERROR_CODE ASIGetExpStatus(int iCameraID, out ExposureStatus pExpStatus);

    public static ExposureStatus GetExposureStatus(int cameraId)
    {
        ExposureStatus result;
        CheckReturn(ASIGetExpStatus(cameraId, out result), MethodBase.GetCurrentMethod(), cameraId);
        return result;
    }


    [DllImport(ASILIBRARY, CallingConvention = CallingConvention.Cdecl)]
    private static extern ASI_ERROR_CODE ASIGetDataAfterExp(int iCameraID, IntPtr pBuffer, long lBuffSize);

    public static bool GetDataAfterExp(int cameraId, IntPtr buffer, long bufferSize)
    {
        var result = ASIGetDataAfterExp(cameraId, buffer, bufferSize);
        if (result == ASI_ERROR_CODE.ASI_ERROR_TIMEOUT)
            return false;

        CheckReturn(result, MethodBase.GetCurrentMethod(), cameraId, buffer, bufferSize);
        return true;
    }

    [DllImport(ASILIBRARY, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ASIGetSerialNumber")]
    private static extern ASI_ERROR_CODE ASIGetSerialNumberNative(int iCameraID, out ASI_SN sn);

    public static ASI_SN GetSerialNumber(int cameraId)
    {
        CheckReturn(ASIGetSerialNumberNative(cameraId, out var sn), MethodBase.GetCurrentMethod(), cameraId);
        return sn;
    }

        // ---------------------------------------------------------------------
        // Additional SDK wrappers (version/product IDs/camera property by ID)
        // ---------------------------------------------------------------------

    [DllImport(ASILIBRARY, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ASIGetSDKVersion")]
    private static extern IntPtr ASIGetSDKVersionNative();

    public static string GetSDKVersion()
    {
        try
        {
            var ptr = ASIGetSDKVersionNative();
            return ptr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(ptr) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    [DllImport(ASILIBRARY, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ASIGetProductIDs")]
    private static extern int ASIGetProductIDsNative([Out] int[] pPIDs);

    public static int[] GetProductIDs()
    {
        // Allocate a reasonable buffer; function returns actual count.
        int[] buffer = new int[64];
        try
        {
            int count = ASIGetProductIDsNative(buffer);
            if (count <= 0) return Array.Empty<int>();
            if (count > buffer.Length) count = buffer.Length; // safety clamp
            return buffer.Take(count).ToArray();
        }
        catch
        {
            return Array.Empty<int>();
        }
    }

    // Get camera property directly by camera ID (not index). Provide 32/64 variants then compat wrapper.
    [DllImport(ASILIBRARY, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ASIGetCameraPropertyByID")]
    private static extern ASI_ERROR_CODE ASIGetCameraPropertyByID32(int iCameraID, out ASI_CAMERA_INFO_32 pASICameraInfo);

    [DllImport(ASILIBRARY, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ASIGetCameraPropertyByID")]
    private static extern ASI_ERROR_CODE ASIGetCameraPropertyByID64(int iCameraID, out ASI_CAMERA_INFO_64 pASICameraInfo);

    public static ASI_CAMERA_INFO_32 GetCameraPropertyByIDCompat(int cameraId)
    {
        if (!Use64Structs)
        {
            ASI_CAMERA_INFO_32 info32;
            CheckReturn(ASIGetCameraPropertyByID32(cameraId, out info32), MethodBase.GetCurrentMethod(), cameraId);
            return info32;
        }
        ASI_CAMERA_INFO_64 info64;
        CheckReturn(ASIGetCameraPropertyByID64(cameraId, out info64), MethodBase.GetCurrentMethod(), cameraId);
        // Reuse conversion logic
        var converted = new ASI_CAMERA_INFO_32
        {
            CameraID = info64.CameraID,
            MaxHeight = (int)Math.Clamp(info64.MaxHeight, int.MinValue, int.MaxValue),
            MaxWidth = (int)Math.Clamp(info64.MaxWidth, int.MinValue, int.MaxValue),
            IsColorCam = info64.IsColorCam,
            BayerPattern = info64.BayerPattern,
            SupportedBins = info64.SupportedBins,
            SupportedVideoFormat = info64.SupportedVideoFormat,
            PixelSize = info64.PixelSize,
            MechanicalShutter = info64.MechanicalShutter,
            ST4Port = info64.ST4Port,
            IsCoolerCam = info64.IsCoolerCam,
            IsUSB3Host = info64.IsUSB3Host,
            IsUSB3Camera = info64.IsUSB3Camera,
            ElecPerADU = info64.ElecPerADU,
            BitDepth = info64.BitDepth,
            IsTriggerCam = info64.IsTriggerCam,
            Unused = info64.Unused
        };
        try
        {
            var nameField64 = typeof(ASI_CAMERA_INFO_64).GetField("name", BindingFlags.NonPublic | BindingFlags.Instance);
            var nameField32 = typeof(ASI_CAMERA_INFO_32).GetField("name", BindingFlags.NonPublic | BindingFlags.Instance);
            if (nameField64 != null && nameField32 != null)
            {
                var nameVal = nameField64.GetValue(info64);
                nameField32.SetValueDirect(__makeref(converted), nameVal);
            }
        }
        catch { }
        return converted;
    }

    // TODO: Serial number support requires exact ASI_SN struct layout from current SDK header.
    // When header available, add:
    // [StructLayout(LayoutKind.Sequential)] struct ASI_SN { ... }
    // [DllImport(ASILIBRARY, CallingConvention=Cdecl, EntryPoint="ASIGetSerialNumber")] static extern ASI_ERROR_CODE ASIGetSerialNumber(int camId, out ASI_SN sn);
    // public static ASI_SN GetSerialNumber(int camId) { ... }

    // TODO: Extend ASI_CONTROL_TYPE & ASI_ERROR_CODE enums with any new members from the latest SDK.
}

//[DataContract]
public class ASICameraException : Exception
{
    /*public ASICameraException(SerializationInfo info, StreamingContext context) : base(info, context)
    {

    }*/

    public ASICameraException(ASICamera2.ASI_ERROR_CODE errorCode) : base(errorCode.ToString())
    {
    }

    public ASICameraException(ASICamera2.ASI_ERROR_CODE errorCode, MethodBase callingMethod, object[] parameters) : base(CreateMessage(errorCode, callingMethod, parameters))
    {
    }

    private static string CreateMessage(ASICamera2.ASI_ERROR_CODE errorCode, MethodBase callingMethod, object[] parameters)
    {
        StringBuilder bld = new StringBuilder();
        bld.AppendLine("Error '" + errorCode + "' from call to ");
        bld.Append("ASI" + callingMethod?.Name + "(");
        var paramNames = callingMethod.GetParameters().Select(x => x.Name);

        if (paramNames is not null)
        {
            foreach (var line in paramNames.Zip(parameters, (s, o) => string.Format("{0}={1}, ", s, o)))
            {
                bld.Append(line);
            }
        }

        bld.Remove(bld.Length - 2, 2);
        bld.Append(')');
        return bld.ToString();
    }
}
}
