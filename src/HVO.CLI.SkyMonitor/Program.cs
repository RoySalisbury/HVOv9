using HVO.ZWOOptical.ASISDK;

namespace HVO.CLI.SkyMonitor;

class Program
{
    static void Main(string[] args)
    {
        var sdkVersion = ASICameraAdaptorFactory.SDKVersion;
        Console.WriteLine($"ZWO ASI SDK Version: {sdkVersion}");

        var cameraCount = ASICameraAdaptorFactory.Count;
        Console.WriteLine($"Number of connected cameras: {cameraCount}");
    }
}

public interface IAllSkyCameraService
{ 
}

public sealed class AllSkyCameraService : IAllSkyCameraService
{
    public AllSkyCameraService()
    {
    }
}

public interface IASICameraAdaptor
{
    int CameraId { get; }
    string Name { get; }
    int MaxWidth { get; }
    int MaxHeight { get; }
    ASICamera2.ASI_BAYER_PATTERN BayerPattern { get; }
}

public sealed class ASICameraAdaptor : IASICameraAdaptor
{
    public int CameraId { get; private set; }
    public string Name { get; private set; }
    public int MaxWidth { get; private set; }
    public int MaxHeight { get; private set; }
    public ASICamera2.ASI_BAYER_PATTERN BayerPattern { get; private set; }

    internal ASICameraAdaptor(int cameraId)
    {
        CameraId = cameraId;

        var camInfo = ASICamera2.GetCameraPropertiesCompat(cameraId);

        Name = camInfo.Name;
        MaxWidth = (int)camInfo.MaxWidth;
        MaxHeight = (int)camInfo.MaxHeight;
        BayerPattern = camInfo.BayerPattern;
    }
}

// public class CameraControl
// {
//     private readonly int _cameraId;
//     private ASICamera2.ASI_CONTROL_CAPS_32 _props;
//     private bool _auto;

//     public CameraControl(int cameraId, int controlIndex)
//     {
//         _cameraId = cameraId;

//         _props = ASICamera2.GetControlCapsCompat(_cameraId, controlIndex);
//         _auto = GetAutoSetting();

//         Console.WriteLine($"Control Name: {_props.Name}, ControlType: {_props.ControlType}, DefaultValue: {_props.DefaultValue}, MinValue: {_props.MinValue}, MaxValue: {_props.MaxValue}, IsAutoSupported: {_props.IsAutoSupported}, Writeable: {_props.IsWritable}, Description: {_props.Description}");
//     }

//     public string Name { get { return _props.Name; } }
//     public string Description { get { return _props.Description; } }
//     public int MinValue { get { return (int)_props.MinValue; } }
//     public int MaxValue { get { return (int)_props.MaxValue; } }
//     public int DefaultValue { get { return (int)_props.DefaultValue; } }
//     public ASICamera2.ASI_CONTROL_TYPE ControlType { get { return _props.ControlType; } }
//     public bool IsAutoAvailable { get { return _props.IsAutoSupported != ASICamera2.ASI_BOOL.ASI_FALSE; } }
//     public bool Writeable { get { return _props.IsWritable != ASICamera2.ASI_BOOL.ASI_FALSE; } }

//     public int Value
//     {
//         get
//         {
//             return (int)ASICamera2.GetControlValueCompat(_cameraId, _props.ControlType, out bool isAuto);
//         }
//         set
//         {
//             ASICamera2.SetControlValueCompat(_cameraId, _props.ControlType, value, IsAuto);
//         }
//     }

//     public bool IsAuto
//     {
//         get
//         {
//             return _auto;
//         }
//         set
//         {
//             _auto = value;
//             ASICamera2.SetControlValue32(_cameraId, _props.ControlType, Value, value);
//         }
//     }

//     private bool GetAutoSetting()
//     {
//         ASICamera2.GetControlValueCompat(_cameraId, _props.ControlType, out bool isAuto);
//         return isAuto;
//     }
// }

public sealed class ASICameraAdaptorFactory
{
    public static string SDKVersion => ASICamera2.GetSDKVersion();

    public static int Count => ASICamera2.GetNumOfConnectedCameras();

    public static IASICameraAdaptor Create(int cameraIndex)
    {
        if (cameraIndex < 0 || cameraIndex >= Count)
        {
            throw new ArgumentOutOfRangeException(nameof(cameraIndex), "Invalid camera index.");
        }

        var cameraAdaptor = Activator.CreateInstance<ASICameraAdaptor>();
        cameraAdaptor.Initialize(cameraIndex);

        return cameraAdaptor;
    }
}

