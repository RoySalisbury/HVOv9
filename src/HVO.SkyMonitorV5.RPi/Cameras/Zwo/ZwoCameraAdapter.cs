#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Skia;
using HVO.SkyMonitorV5.RPi.Pipeline.Preprocessing;
using HVO.ZWOOptical.ASISDK;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkiaSharp;
using static HVO.ZWOOptical.ASISDK.ASICamera2;

namespace HVO.SkyMonitorV5.RPi.Cameras.Zwo;

/// <summary>
/// Native camera adapter for ZWO ASI devices. Frames are captured directly from the ASICamera2 SDK into
/// unmanaged buffers that back <see cref="SKBitmap"/> instances without additional copies.
/// </summary>
public sealed class ZwoCameraAdapter : CameraAdapterBase
{
    private const int DefaultBin = 1;
    private const int CaptureTimeoutPaddingMs = 150;
    private const int MinimumCaptureWaitMs = 50;
    private const int MaximumCaptureWaitMs = 30_000;
    private const int MaxCaptureRetries = 2;

    private readonly IObservatoryClock _clock;
    private readonly IOptionsMonitor<ObservatoryLocationOptions> _locationMonitor;
    private readonly IOptionsMonitor<CardinalDirectionsOptions> _cardinalMonitor;
    private readonly ILoggerFactory? _loggerFactory;

    private readonly Dictionary<ASI_CONTROL_TYPE, ControlCapability> _controlCaps = new();

    private ZwoCaptureArea? _captureArea;
    private int _cameraIndex = -1;
    private int _cameraId = -1;
    private string _cameraName = string.Empty;
    private bool _isColorCamera;
    private bool _videoStreaming;
    private bool _cameraOpen;

    private ExposureSettings? _lastExposureSettings;

    private static readonly SKColorSpace LinearSrgbColorSpace = SKColorSpace.CreateSrgbLinear();

    public ZwoCameraAdapter(
        RigSpec rig,
        IObservatoryClock clock,
        IOptionsMonitor<ObservatoryLocationOptions> locationMonitor,
        IOptionsMonitor<CardinalDirectionsOptions> cardinalMonitor,
        ILoggerFactory? loggerFactory,
        ILogger<ZwoCameraAdapter>? logger = null,
        IFramePreprocessingOrchestrator? preprocessingOrchestrator = null)
        : base(
            EnsureRigDescriptor(rig),
            clock,
            logger ?? loggerFactory?.CreateLogger<ZwoCameraAdapter>() ?? NullLogger<ZwoCameraAdapter>.Instance,
            preprocessingOrchestrator)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _locationMonitor = locationMonitor ?? throw new ArgumentNullException(nameof(locationMonitor));
        _cardinalMonitor = cardinalMonitor ?? throw new ArgumentNullException(nameof(cardinalMonitor));
        _loggerFactory = loggerFactory;
    }

    protected override Task<Result<bool>> OnInitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var selectionResult = SelectCamera();
            if (selectionResult.IsFailure)
            {
                return Task.FromResult(Result<bool>.Failure(selectionResult.Error ?? new InvalidOperationException("Unable to select ZWO camera.")));
            }

            var cameraInfo = selectionResult.Value;
            _cameraIndex = cameraInfo.Index;
            _cameraId = cameraInfo.Info.CameraID;
            _cameraName = cameraInfo.Info.Name;
            _isColorCamera = cameraInfo.Info.IsColorCam == ASI_BOOL.ASI_TRUE;

            OpenHardwareCamera();
            LoadControlCapabilities();
            ConfigureCaptureArea(cameraInfo.Info);
            StartStreaming();

            Logger.LogInformation(
                "ZWO camera {CameraName} (ID {CameraId}) initialized on index {CameraIndex}. Max {Width}x{Height}px, Color={IsColor}.",
                _cameraName,
                _cameraId,
                _cameraIndex,
                cameraInfo.Info.MaxWidth,
                cameraInfo.Info.MaxHeight,
                _isColorCamera);

            return Task.FromResult(Result<bool>.Success(true));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to initialize ZWO camera adapter for rig {RigName}.", Rig.Name);
            return Task.FromResult(Result<bool>.Failure(ex));
        }
    }

    protected override Task<Result<bool>> OnShutdownAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopStreaming();
            CloseHardwareCamera();
            ResetState();
            Logger.LogInformation("ZWO camera adapter shutdown complete for rig {RigName}.", Rig.Name);
            return Task.FromResult(Result<bool>.Success(true));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error while shutting down ZWO camera adapter for rig {RigName}.", Rig.Name);
            return Task.FromResult(Result<bool>.Failure(ex));
        }
    }

    protected override Task<Result<ExposureSettings>> ConfigureExposureAsync(ExposureSettings requestedExposure, CancellationToken cancellationToken)
    {
        if (_cameraId < 0)
        {
            var error = new InvalidOperationException("Camera has not been initialized.");
            return Task.FromResult(Result<ExposureSettings>.Failure(error));
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var effectiveExposureMs = Math.Max(requestedExposure.ExposureMilliseconds, 1);
            var exposureControl = GetControlCapability(ASI_CONTROL_TYPE.ASI_EXPOSURE);
            var gainControl = GetControlCapability(ASI_CONTROL_TYPE.ASI_GAIN);

            var exposureMicroseconds = Math.Clamp(effectiveExposureMs * 1000, exposureControl.MinValue, exposureControl.MaxValue);
            var gainValue = Math.Clamp(requestedExposure.Gain, gainControl.MinValue, gainControl.MaxValue);

            SetControlValueCompat(_cameraId, ASI_CONTROL_TYPE.ASI_EXPOSURE, exposureMicroseconds, requestedExposure.AutoExposure && exposureControl.SupportsAuto);

            if (Rig.Camera.Capabilities.SupportsGainControl)
            {
                SetControlValueCompat(_cameraId, ASI_CONTROL_TYPE.ASI_GAIN, gainValue, requestedExposure.AutoGain && gainControl.SupportsAuto);
            }

            var applied = new ExposureSettings(
                ExposureMilliseconds: exposureMicroseconds / 1000,
                Gain: gainValue,
                AutoExposure: requestedExposure.AutoExposure && exposureControl.SupportsAuto,
                AutoGain: requestedExposure.AutoGain && gainControl.SupportsAuto);

            _lastExposureSettings = applied;

            Logger.LogTrace(
                "Applied exposure {ExposureMs} ms, gain {Gain}, auto={AutoExposure}, autoGain={AutoGain}.",
                applied.ExposureMilliseconds,
                applied.Gain,
                applied.AutoExposure,
                applied.AutoGain);

            return Task.FromResult(Result<ExposureSettings>.Success(applied));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to apply exposure settings for rig {RigName}.", Rig.Name);
            return Task.FromResult(Result<ExposureSettings>.Failure(ex));
        }
    }

    protected override Task<Result<AdapterFrame>> AcquireImageAsync(ExposureSettings exposure, CancellationToken cancellationToken)
    {
        if (_cameraId < 0 || _captureArea is null)
        {
            var error = new InvalidOperationException("Camera is not ready for capture.");
            Logger.LogError(error, "Capture requested before initialization for rig {RigName}.", Rig.Name);
            return Task.FromResult(Result<AdapterFrame>.Failure(error));
        }

        FrameBufferLease? lease = null;
        SKBitmap? bitmap = null;
        SkiaPixelLease? pixelLease = null;
        SKImage? immutableImage = null;
        StarFieldEngine? engine = null;
        var captureSucceeded = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var captureArea = _captureArea.Value;
            var waitMs = ComputeWaitTime(exposure);
            lease = FrameBufferLease.Rent(captureArea.CaptureBufferSizeBytes);

            if (!TryAcquireFrame(lease.Pointer, captureArea.CaptureBufferSizeBytes, waitMs, cancellationToken))
            {
                throw new TimeoutException($"Timed out retrieving video frame from ZWO camera {_cameraName} after {waitMs} ms.");
            }

            bitmap = CreateBitmapFromCapture(lease, captureArea);
            pixelLease = SkiaPixelLease.FromBitmap(bitmap, disposeBitmap: false);

            if (lease is not null && lease.IsAllocated)
            {
                immutableImage = CreateImmutableImageFromLease(lease, captureArea);
                lease = null; // ownership transferred to zero-copy structures
            }
            else
            {
                lease = null;
            }

            var captureInstant = _clock.UtcNow;
            var location = _locationMonitor.CurrentValue;
            var flipHorizontal = _cardinalMonitor.CurrentValue.SwapEastWest;

            engine = new StarFieldEngine(
                Rig,
                latitudeDeg: location.LatitudeDegrees,
                longitudeDeg: location.LongitudeDegrees,
                utcUtc: captureInstant.UtcDateTime,
                flipHorizontal: flipHorizontal,
                applyRefraction: true,
                horizonPadding: MockCameraAdapter.DefaultHorizonPadding);

            var frame = new AdapterFrame(
                Bitmap: bitmap,
                PixelLease: pixelLease,
                ImmutableImage: immutableImage,
                Surface: null,
                Engine: engine,
                Timestamp: captureInstant,
                LatitudeDeg: location.LatitudeDegrees,
                LongitudeDeg: location.LongitudeDegrees,
                FlipHorizontal: flipHorizontal,
                HorizonPadding: MockCameraAdapter.DefaultHorizonPadding,
                ApplyRefraction: true,
                Exposure: exposure);

            bitmap = null;
            immutableImage = null;
            pixelLease = null;
            engine = null;
            captureSucceeded = true;

            return Task.FromResult(Result<AdapterFrame>.Success(frame));
        }
        catch (OperationCanceledException ex)
        {
            Logger.LogDebug(ex, "Capture cancelled for ZWO camera {CameraName}.", _cameraName);
            return Task.FromResult(Result<AdapterFrame>.Failure(ex));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to capture frame from ZWO camera {CameraName}.", _cameraName);
            return Task.FromResult(Result<AdapterFrame>.Failure(ex));
        }
        finally
        {
            if (!captureSucceeded)
            {
                lease?.Dispose();
                bitmap?.Dispose();
                immutableImage?.Dispose();
                pixelLease?.Dispose();
                engine?.Dispose();
            }
        }
    }

    protected override Task<Result<AdapterFrame>> PostprocessFrameAsync(AdapterFrame frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (frame.ImmutableImage is not null)
        {
            return Task.FromResult(Result<AdapterFrame>.Success(frame));
        }

        using var pixmap = frame.Bitmap.PeekPixels();
        if (pixmap is null)
        {
            return Task.FromResult(Result<AdapterFrame>.Success(frame with { ImmutableImage = null }));
        }

        var immutable = SKImage.FromPixels(pixmap);
        var updated = frame with { ImmutableImage = immutable };
        return Task.FromResult(Result<AdapterFrame>.Success(updated));
    }

    private Result<CameraSelection> SelectCamera()
    {
        var connected = Math.Max(GetNumOfConnectedCameras(), 0);
        if (connected == 0)
        {
            return Result<CameraSelection>.Failure(new InvalidOperationException("No ZWO cameras detected."));
        }

        var desiredNames = new[]
        {
            Rig.Camera.Descriptor.Model,
            Rig.Camera.Name,
            Rig.Name
        };

        CameraSelection? fallback = null;

        for (var index = 0; index < connected; index++)
        {
            var info = GetCameraPropertiesCompat(index);
            fallback ??= new CameraSelection(index, info);

            if (MatchesDescriptor(info.Name, desiredNames))
            {
                return Result<CameraSelection>.Success(new CameraSelection(index, info));
            }
        }

        Logger.LogWarning(
            "Rig {RigName} requested camera '{Requested}', but no direct match was found. Falling back to index 0 ({Fallback}).",
            Rig.Name,
            string.Join(",", desiredNames.Where(n => !string.IsNullOrWhiteSpace(n))),
            fallback?.Info.Name ?? "unknown");

        return fallback is null
            ? Result<CameraSelection>.Failure(new InvalidOperationException("Unable to retrieve camera properties."))
            : Result<CameraSelection>.Success(fallback.Value);
    }

    private void OpenHardwareCamera()
    {
        if (_cameraOpen)
        {
            return;
        }

    ASICamera2.OpenCamera(_cameraId);
    InitCamera(_cameraId);
        _cameraOpen = true;
    }

    private void StartStreaming()
    {
        if (_videoStreaming)
        {
            return;
        }

        StartVideoCapture(_cameraId);
        _videoStreaming = true;
    }

    private void StopStreaming()
    {
        if (!_videoStreaming)
        {
            return;
        }

        try
        {
            StopVideoCapture(_cameraId);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error stopping video capture for camera {CameraName}.", _cameraName);
        }
        finally
        {
            _videoStreaming = false;
        }
    }

    private void CloseHardwareCamera()
    {
        if (!_cameraOpen)
        {
            return;
        }

        try
        {
            ASICamera2.CloseCamera(_cameraId);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error while closing camera {CameraName}.", _cameraName);
        }
        finally
        {
            _cameraOpen = false;
        }
    }

    private void ResetState()
    {
        _captureArea = null;
        _cameraIndex = -1;
        _cameraId = -1;
        _cameraName = string.Empty;
        _isColorCamera = false;
        _lastExposureSettings = null;
        _controlCaps.Clear();
    }

    private void LoadControlCapabilities()
    {
        _controlCaps.Clear();

        var count = GetNumOfControls(_cameraId);
        for (var i = 0; i < count; i++)
        {
            var caps = GetControlCapsCompat(_cameraId, i);
            var capability = new ControlCapability(
                MinValue: caps.MinValue,
                MaxValue: caps.MaxValue,
                SupportsAuto: caps.IsAutoSupported == ASI_BOOL.ASI_TRUE);

            _controlCaps[caps.ControlType] = capability;
        }

        Logger.LogDebug(
            "Loaded {ControlCount} control capabilities for camera {CameraName}.",
            _controlCaps.Count,
            _cameraName);
    }

    private ControlCapability GetControlCapability(ASI_CONTROL_TYPE controlType)
    {
        if (_controlCaps.TryGetValue(controlType, out var capability))
        {
            return capability;
        }

        // Exposure ranges are large in practice; provide permissive defaults if SDK query fails.
        return controlType switch
        {
            ASI_CONTROL_TYPE.ASI_EXPOSURE => new ControlCapability(32, 60_000_000, true),
            ASI_CONTROL_TYPE.ASI_GAIN => new ControlCapability(0, 600, true),
            _ => new ControlCapability(int.MinValue, int.MaxValue, false)
        };
    }

    private void ConfigureCaptureArea(ASI_CAMERA_INFO_32 info)
    {
        var maxSize = new Size(info.MaxWidth, info.MaxHeight);
        var imageType = SelectImageType(info.SupportedVideoFormat);
        var bin = DefaultBin;

        SetROIFormat(_cameraId, maxSize, bin, imageType);
        SetStartPos(_cameraId, new Point(0, 0));

        var imageInfo = CreateImageInfo(maxSize, imageType);
        var captureRowBytes = CalculateCaptureRowBytes(maxSize.Width, imageType);
        var captureBufferSize = checked((long)captureRowBytes * maxSize.Height);
        var outputRowBytes = Math.Max(imageInfo.BytesPerPixel * maxSize.Width, 1);

        _captureArea = new ZwoCaptureArea(maxSize, new Point(0, 0), bin, imageType, imageInfo, captureRowBytes, outputRowBytes, captureBufferSize);

        Logger.LogInformation(
            "Configured capture area {Width}x{Height}px, bin {Bin}, format {ImageType} (captureRowBytes={CaptureRowBytes}, outputRowBytes={OutputRowBytes}).",
            maxSize.Width,
            maxSize.Height,
            bin,
            imageType,
            captureRowBytes,
            outputRowBytes);
    }

    private static SKImageInfo CreateImageInfo(Size size, ASI_IMG_TYPE imageType)
    {
        return imageType switch
        {
            ASI_IMG_TYPE.ASI_IMG_RGB24 => new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul),
            _ => new SKImageInfo(size.Width, size.Height, SKColorType.Gray8, SKAlphaType.Opaque)
        };
    }

    private static int CalculateCaptureRowBytes(int width, ASI_IMG_TYPE imageType)
    {
        return imageType switch
        {
            ASI_IMG_TYPE.ASI_IMG_RGB24 => width * 3,
            ASI_IMG_TYPE.ASI_IMG_RAW16 => width * 2,
            _ => width
        };
    }

    private ASI_IMG_TYPE SelectImageType(IReadOnlyList<ASI_IMG_TYPE> supportedFormats)
    {
        if (supportedFormats.Contains(ASI_IMG_TYPE.ASI_IMG_Y8))
        {
            return ASI_IMG_TYPE.ASI_IMG_Y8;
        }

        if (supportedFormats.Contains(ASI_IMG_TYPE.ASI_IMG_RAW16))
        {
            Logger.LogWarning("Camera {CameraName} does not expose Y8; RAW16 frames will be down-converted to 8-bit for now.", _cameraName);
            return ASI_IMG_TYPE.ASI_IMG_RAW16;
        }

        if (supportedFormats.Contains(ASI_IMG_TYPE.ASI_IMG_RGB24))
        {
            Logger.LogWarning("Camera {CameraName} only exposes RGB24; this path will incur a conversion copy.", _cameraName);
            return ASI_IMG_TYPE.ASI_IMG_RGB24;
        }

        Logger.LogWarning("Unsupported video format set detected; defaulting to Y8.");
        return ASI_IMG_TYPE.ASI_IMG_Y8;
    }

    private static bool MatchesDescriptor(string candidate, IEnumerable<string?> expectedNames)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        return expectedNames.Any(expected =>
            !string.IsNullOrWhiteSpace(expected) &&
            candidate.Equals(expected, StringComparison.OrdinalIgnoreCase));
    }

    private static int ComputeWaitTime(ExposureSettings exposure)
    {
        var requested = exposure.ExposureMilliseconds > 0 ? exposure.ExposureMilliseconds : 1000;
        var wait = requested + CaptureTimeoutPaddingMs;
        return Math.Clamp(wait, MinimumCaptureWaitMs, MaximumCaptureWaitMs);
    }

    private bool TryAcquireFrame(IntPtr buffer, long bufferSizeBytes, int waitMs, CancellationToken cancellationToken)
    {
        var attempts = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            attempts++;
            var success = GetVideoData(_cameraId, buffer, bufferSizeBytes, waitMs);
            if (success)
            {
                if (attempts > 1)
                {
                    Logger.LogDebug("Video data acquisition succeeded on retry {Attempt} for camera {CameraName}.", attempts, _cameraName);
                }

                return true;
            }

            if (attempts >= MaxCaptureRetries)
            {
                Logger.LogWarning("Video data acquisition timed out after {Attempts} attempts for camera {CameraName}.", attempts, _cameraName);
                return false;
            }
        }
    }

    private SKBitmap CreateBitmapFromCapture(FrameBufferLease lease, ZwoCaptureArea captureArea)
    {
        switch (captureArea.ImageType)
        {
            case ASI_IMG_TYPE.ASI_IMG_Y8:
                return InstallZeroCopyBitmap(lease, captureArea.ImageInfo, captureArea.OutputRowBytes);
            case ASI_IMG_TYPE.ASI_IMG_RGB24:
            {
                var bitmap = ConvertRgb24ToBgraBitmap(lease.Pointer, captureArea.Size.Width, captureArea.Size.Height, captureArea.CaptureRowBytes);
                lease.Dispose();
                return bitmap;
            }
            case ASI_IMG_TYPE.ASI_IMG_RAW16:
            {
                var bitmap = ConvertRaw16ToGrayBitmap(lease.Pointer, captureArea.Size.Width, captureArea.Size.Height, captureArea.CaptureRowBytes);
                lease.Dispose();
                return bitmap;
            }
            default:
            {
                var bitmap = ConvertY8ToBitmap(lease.Pointer, captureArea.Size.Width, captureArea.Size.Height, captureArea.CaptureRowBytes);
                lease.Dispose();
                return bitmap;
            }
        }
    }

    private static SKImage? CreateImmutableImageFromLease(FrameBufferLease lease, ZwoCaptureArea captureArea)
    {
        if (!lease.IsAllocated)
        {
            return null;
        }

        lease.AddRef();

        var baseInfo = captureArea.ImageInfo;
        var info = new SKImageInfo(
            baseInfo.Width,
            baseInfo.Height,
            baseInfo.ColorType,
            baseInfo.AlphaType,
            LinearSrgbColorSpace);

        using var pixmap = new SKPixmap(info, lease.Pointer, captureArea.OutputRowBytes);
        var image = SKImage.FromPixels(pixmap, FrameBufferLease.ReleasePixels, lease);
        if (image is null)
        {
            lease.Release();
        }

        return image;
    }

    private static SKBitmap InstallZeroCopyBitmap(FrameBufferLease lease, SKImageInfo imageInfo, int rowBytes)
    {
        var bitmap = new SKBitmap();
        if (!bitmap.InstallPixels(imageInfo, lease.Pointer, rowBytes, FrameBufferLease.ReleasePixels, lease))
        {
            lease.Dispose();
            throw new InvalidOperationException($"Failed to install pixels for captured frame ({imageInfo.Width}x{imageInfo.Height}).");
        }

        return bitmap;
    }

    private static SKBitmap ConvertRgb24ToBgraBitmap(IntPtr sourceBuffer, int width, int height, int captureRowBytes)
    {
        var bufferLength = captureRowBytes * height;
        var rental = ArrayPool<byte>.Shared.Rent(bufferLength);
        try
        {
            Marshal.Copy(sourceBuffer, rental, 0, bufferLength);

            var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
            var destination = bitmap.GetPixelSpan();
            for (var row = 0; row < height; row++)
            {
                var srcOffset = row * captureRowBytes;
                var destOffset = row * width * 4;
                var srcIndex = 0;
                var destIndex = 0;
                var srcSpan = rental.AsSpan(srcOffset, captureRowBytes);
                var destSpan = destination.Slice(destOffset, width * 4);

                for (var col = 0; col < width; col++)
                {
                    if (srcIndex + 2 >= srcSpan.Length || destIndex + 3 >= destSpan.Length)
                    {
                        break;
                    }

                    var r = srcSpan[srcIndex++];
                    var g = srcSpan[srcIndex++];
                    var b = srcSpan[srcIndex++];

                    destSpan[destIndex++] = b;
                    destSpan[destIndex++] = g;
                    destSpan[destIndex++] = r;
                    destSpan[destIndex++] = 255;
                }
            }

            return bitmap;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rental, clearArray: false);
        }
    }

    private static SKBitmap ConvertRaw16ToGrayBitmap(IntPtr sourceBuffer, int width, int height, int captureRowBytes)
    {
        var bufferLength = captureRowBytes * height;
        var rental = ArrayPool<byte>.Shared.Rent(bufferLength);
        try
        {
            Marshal.Copy(sourceBuffer, rental, 0, bufferLength);

            var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque));
            var destination = bitmap.GetPixelSpan();
            for (var row = 0; row < height; row++)
            {
                var srcOffset = row * captureRowBytes;
                var destOffset = row * width;
                for (var col = 0; col < width; col++)
                {
                    var sampleOffset = srcOffset + (col * 2);
                    if (sampleOffset + 1 >= rental.Length)
                    {
                        break;
                    }

                    var low = rental[sampleOffset];
                    var high = rental[sampleOffset + 1];
                    var value = (ushort)(low | (high << 8));
                    destination[destOffset + col] = (byte)(value >> 8);
                }
            }

            return bitmap;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rental, clearArray: false);
        }
    }

    private static SKBitmap ConvertY8ToBitmap(IntPtr sourceBuffer, int width, int height, int captureRowBytes)
    {
        var bufferLength = captureRowBytes * height;
        var rental = ArrayPool<byte>.Shared.Rent(bufferLength);
        try
        {
            Marshal.Copy(sourceBuffer, rental, 0, bufferLength);

            var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque));
            var destination = bitmap.GetPixelSpan();
            for (var row = 0; row < height; row++)
            {
                var srcOffset = row * captureRowBytes;
                var destOffset = row * width;
                var copyLength = Math.Min(width, captureRowBytes);
                rental.AsSpan(srcOffset, copyLength).CopyTo(destination.Slice(destOffset, copyLength));
            }
            return bitmap;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rental, clearArray: false);
        }
    }

    private static RigSpec EnsureRigDescriptor(RigSpec rig)
    {
        if (!string.Equals(rig.Camera.Descriptor.Manufacturer, "Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return rig;
        }

        var descriptor = new CameraDescriptor(
            Manufacturer: "ZWO",
            Model: rig.Name,
            DriverVersion: "unversioned",
            AdapterName: nameof(ZwoCameraAdapter),
            Capabilities: new[] { "Native", "StackingCompatible", "HighSpeed" });

        return rig with
        {
            Camera = rig.Camera with { Descriptor = descriptor }
        };
    }

    private readonly record struct CameraSelection(int Index, ASI_CAMERA_INFO_32 Info);

    private readonly record struct ControlCapability(int MinValue, int MaxValue, bool SupportsAuto);

    private readonly record struct ZwoCaptureArea(
        Size Size,
        Point StartPosition,
        int Bin,
        ASI_IMG_TYPE ImageType,
        SKImageInfo ImageInfo,
        int CaptureRowBytes,
        int OutputRowBytes,
        long CaptureBufferSizeBytes);

    private sealed class FrameBufferLease : IDisposable
    {
        private IntPtr _pointer;
        private readonly long _length;
        private int _refCount;

        private FrameBufferLease(long length)
        {
            if (length <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            _length = length;
            _pointer = Marshal.AllocHGlobal(new IntPtr(length));
            _refCount = 1;
        }

        public IntPtr Pointer => _pointer;

        public long Length => _length;

        public bool IsAllocated => _pointer != IntPtr.Zero;

        public static FrameBufferLease Rent(long length) => new(length);

        public void AddRef()
        {
            if (_pointer == IntPtr.Zero)
            {
                throw new ObjectDisposedException(nameof(FrameBufferLease));
            }

            Interlocked.Increment(ref _refCount);
        }

        public void Dispose() => Release();

        public void Release()
        {
            if (Interlocked.Decrement(ref _refCount) == 0)
            {
                var ptr = Interlocked.Exchange(ref _pointer, IntPtr.Zero);
                if (ptr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }
        }

        public static void ReleasePixels(IntPtr address, object context)
        {
            if (context is FrameBufferLease lease)
            {
                lease.Release();
            }
        }
    }
}
