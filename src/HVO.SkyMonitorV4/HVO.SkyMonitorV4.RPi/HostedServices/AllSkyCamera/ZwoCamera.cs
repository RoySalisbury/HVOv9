#nullable enable

using System.Collections.Concurrent;
using System.Drawing;
using HVO.ZWOOptical.ASISDK;

namespace HVO.SkyMonitorV4.RPi.HostedServices.AllSkyCamera;

internal static class ZwoCameraManager
{
    public static int Count => ASICamera2.GetNumOfConnectedCameras();

    public static ZwoCamera Create(int cameraIndex)
    {
        if (cameraIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cameraIndex));
        }

        var count = Count;
        if (cameraIndex >= count)
        {
            throw new ArgumentOutOfRangeException(nameof(cameraIndex), cameraIndex, "Requested camera index exceeds available cameras");
        }

    var info = ASICamera2.GetCameraPropertiesCompat(cameraIndex);
    return new ZwoCamera(cameraIndex, info.CameraID, info.Name, info.MaxWidth, info.MaxHeight, info.IsColorCam == ASICamera2.ASI_BOOL.ASI_TRUE);
    }
}

internal sealed class ZwoCamera : IDisposable
{
    private readonly int _cameraIndex;
    private readonly int _cameraId;
    private readonly string _name;
    private readonly int _maxWidth;
    private readonly int _maxHeight;
    private readonly bool _isColor;
    private readonly ConcurrentDictionary<ASICamera2.ASI_CONTROL_TYPE, CameraControl> _controls = new();
    private bool _isOpen;
    private bool _isCapturing;
    private CaptureAreaInfo? _captureArea;

    public ZwoCamera(int cameraIndex, int cameraId, string name, int maxWidth, int maxHeight, bool isColor)
    {
        _cameraIndex = cameraIndex;
        _cameraId = cameraId;
        _name = name;
        _maxWidth = maxWidth;
        _maxHeight = maxHeight;
        _isColor = isColor;
    }

    public int CameraIndex => _cameraIndex;
    public int CameraId => _cameraId;
    public string Name => _name;
    public int MaxWidth => _maxWidth;
    public int MaxHeight => _maxHeight;
    public bool IsColor => _isColor;

    public IReadOnlyDictionary<ASICamera2.ASI_CONTROL_TYPE, CameraControl> Controls => _controls;

    public void Open()
    {
        if (_isOpen)
        {
            return;
        }

        ASICamera2.OpenCamera(_cameraId);
        ASICamera2.InitCamera(_cameraId);

        LoadControls();
        _isOpen = true;
    }

    public void Close()
    {
        if (!_isOpen)
        {
            return;
        }

        try
        {
            if (_isCapturing)
            {
                ASICamera2.StopVideoCapture(_cameraId);
                _isCapturing = false;
            }
        }
        catch
        {
            // Swallow exceptions during shutdown
        }

        ASICamera2.CloseCamera(_cameraId);
        _isOpen = false;
    }

    public void Dispose()
    {
        Close();
        GC.SuppressFinalize(this);
    }

    public void StartVideoCapture()
    {
        EnsureOpen();
        ASICamera2.StartVideoCapture(_cameraId);
        _isCapturing = true;
    }

    public void StopVideoCapture()
    {
        if (!_isOpen || !_isCapturing)
        {
            return;
        }

        ASICamera2.StopVideoCapture(_cameraId);
        _isCapturing = false;
    }

    public bool TryGetVideoData(IntPtr buffer, long bufferSize, int waitMs)
    {
        EnsureOpen();
        return ASICamera2.GetVideoData(_cameraId, buffer, bufferSize, waitMs);
    }

    public void SetStartPosition(Point start)
    {
        EnsureOpen();
        ASICamera2.SetStartPos(_cameraId, start);
    }

    public int GetDroppedFrames()
    {
        EnsureOpen();
        return ASICamera2.GetDroppedFrames(_cameraId);
    }

    public CaptureAreaInfo? CaptureArea => _captureArea;

    public CaptureAreaInfo SetCaptureArea(CaptureAreaInfo area)
    {
        EnsureOpen();
        ASICamera2.SetROIFormat(_cameraId, area.Size, area.Bin, area.ImageType);
        _captureArea = area;
        return area;
    }

    public bool TryGetControl(ASICamera2.ASI_CONTROL_TYPE controlType, out CameraControl? control)
    {
        EnsureOpen();
        return _controls.TryGetValue(controlType, out control);
    }

    private void EnsureOpen()
    {
        if (!_isOpen)
        {
            throw new InvalidOperationException("Camera has not been opened. Call Open() before performing this operation.");
        }
    }

    private void LoadControls()
    {
        _controls.Clear();

        var controlCount = ASICamera2.GetNumOfControls(_cameraId);
        for (var i = 0; i < controlCount; i++)
        {
            var caps = ASICamera2.GetControlCapsCompat(_cameraId, i);
            var control = new CameraControl(_cameraId, caps);
            _controls[caps.ControlType] = control;
        }
    }
}

internal sealed class CameraControl
{
    private readonly int _cameraId;
    private readonly ASICamera2.ASI_CONTROL_CAPS_32 _caps;

    public CameraControl(int cameraId, ASICamera2.ASI_CONTROL_CAPS_32 caps)
    {
        _cameraId = cameraId;
        _caps = caps;
    }

    public ASICamera2.ASI_CONTROL_TYPE ControlType => _caps.ControlType;
    public string Name => _caps.Name;
    public string Description => _caps.Description;
    public int MinValue => _caps.MinValue;
    public int MaxValue => _caps.MaxValue;
    public int DefaultValue => _caps.DefaultValue;
    public bool SupportsAuto => _caps.IsAutoSupported == ASICamera2.ASI_BOOL.ASI_TRUE;

    public bool IsAuto
    {
        get
        {
            var (_, isAuto) = Read();
            return isAuto;
        }
        set
        {
            var (currentValue, _) = Read();
            Write(currentValue, value);
        }
    }

    public int Value
    {
        get
        {
            var (value, _) = Read();
            return value;
        }
        set
        {
            var (_, isAuto) = Read();
            Write(value, isAuto);
        }
    }

    public void SetValue(int value, bool isAuto)
    {
        Write(value, isAuto);
    }

    public void ResetToDefault()
    {
        ASICamera2.SetControlValueCompat(_cameraId, ControlType, DefaultValue, false);
    }

    private (int Value, bool IsAuto) Read()
    {
        var value = ASICamera2.GetControlValueCompat(_cameraId, ControlType, out var isAuto);
        return (value, isAuto);
    }

    private void Write(int value, bool isAuto)
    {
        var clamped = Math.Clamp(value, MinValue, MaxValue);
        ASICamera2.SetControlValueCompat(_cameraId, ControlType, clamped, isAuto && SupportsAuto);
    }
}

internal readonly record struct CaptureAreaInfo(Size Size, Point StartPosition, int Bin, ASICamera2.ASI_IMG_TYPE ImageType)
{
    public long CalculateImageBufferSizeBytes()
    {
        var pixelCount = (long)Size.Width * Size.Height;
        return ImageType switch
        {
            ASICamera2.ASI_IMG_TYPE.ASI_IMG_RAW16 => pixelCount * sizeof(ushort),
            ASICamera2.ASI_IMG_TYPE.ASI_IMG_RGB24 => pixelCount * 3,
            _ => pixelCount
        };
    }
}
