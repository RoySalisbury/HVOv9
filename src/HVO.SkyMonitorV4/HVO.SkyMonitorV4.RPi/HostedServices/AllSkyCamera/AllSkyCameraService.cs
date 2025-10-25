#nullable enable
#pragma warning disable CA1416

using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using HVO.ZWOOptical.ASISDK;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.SkyMonitorV4.RPi.HostedServices.AllSkyCamera;

public sealed class AllSkyCameraService : IAllSkyCameraService
{
    private readonly ILogger<AllSkyCameraService> _logger;
    private readonly AllSkyCameraServiceOptions _options;

    private readonly object _stateSync = new();
    private (DateTimeOffset Timestamp, string? RelativePath) _lastImageMetadata = (DateTimeOffset.MinValue, null);

    private volatile bool _isRecording;

    private CameraControl? _exposureBrightnessControl;
    private CameraControl? _exposureGainControl;
    private CameraControl? _exposureDurationControl;
    private CameraControl? _autoExposureMaxGainControl;
    private CameraControl? _autoExposureMaxDurationControl;
    private CameraControl? _autoExposureTargetBrightnessControl;
    private CameraControl? _bandwidthControl;

    private string _cacheRoot;
    private bool _cacheFallbackLogged;

    private bool _autoExposureGain;
    private int _exposureGain;
    private bool _autoExposureDuration;
    private int _exposureDurationMilliseconds;
    private bool _autoExposureBrightness;
    private int _exposureBrightness;
    private int _autoExposureMaxDurationMilliseconds;
    private int _autoExposureMaxGain;
    private int _autoExposureBrightnessTarget;
    private double _maxAttemptedFps;
    private double _imageCircleRotationAngle;

    private readonly ImageCodecInfo _jpegEncoder;

    public AllSkyCameraService(ILogger<AllSkyCameraService> logger, IOptions<AllSkyCameraServiceOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        _autoExposureGain = _options.AutoExposureGain;
        _exposureGain = _options.ExposureGain;
        _autoExposureDuration = _options.AutoExposureDuration;
        _exposureDurationMilliseconds = _options.ExposureDurationMs;
        _autoExposureBrightness = _options.AutoExposureBrightness;
        _exposureBrightness = _options.ExposureBrightness;
        _autoExposureMaxDurationMilliseconds = _options.AutoExposureMaxDurationMs;
        _autoExposureMaxGain = _options.AutoExposureMaxGain;
        _autoExposureBrightnessTarget = _options.AutoExposureTargetBrightness;
        _maxAttemptedFps = _options.MaxAttemptedFps;
        _imageCircleRotationAngle = _options.ImageCircleRotationAngle;
        _cacheRoot = _options.CacheImageSaveRoot;

        _jpegEncoder = ImageCodecInfo.GetImageEncoders().FirstOrDefault(x => x.FormatID == ImageFormat.Jpeg.Guid)
            ?? throw new InvalidOperationException("Unable to locate JPEG encoder on this platform.");
    }

    public bool IsRecording => _isRecording;

    public DateTimeOffset LastImageTakenTimestamp
    {
        get
        {
            lock (_stateSync)
            {
                return _lastImageMetadata.Timestamp;
            }
        }
    }

    public string? LastImageRelativePath
    {
        get
        {
            lock (_stateSync)
            {
                return _lastImageMetadata.RelativePath;
            }
        }
    }

    public string ImageCacheRoot => _cacheRoot;

    public bool AutoExposureGain
    {
        get => _autoExposureGain;
        set
        {
            if (_autoExposureGain == value)
            {
                return;
            }

            _autoExposureGain = value;
            ApplyExposureGainControl();
        }
    }

    public bool AutoExposureDuration
    {
        get => _autoExposureDuration;
        set
        {
            if (_autoExposureDuration == value)
            {
                return;
            }

            _autoExposureDuration = value;
            ApplyExposureDurationControl();
        }
    }

    public bool AutoExposureBrightness
    {
        get => _autoExposureBrightness;
        set
        {
            if (_autoExposureBrightness == value)
            {
                return;
            }

            _autoExposureBrightness = value;
            ApplyExposureBrightnessControl();
        }
    }

    public int ExposureGain
    {
        get => _exposureGain;
        set
        {
            if (_exposureGain == value)
            {
                return;
            }

            _exposureGain = value;
            ApplyExposureGainControl();
        }
    }

    public int ExposureDurationMilliseconds
    {
        get => _exposureDurationMilliseconds;
        set
        {
            if (_exposureDurationMilliseconds == value)
            {
                return;
            }

            _exposureDurationMilliseconds = Math.Max(1, value);
            ApplyExposureDurationControl();
        }
    }

    public int AutoExposureMaxDurationMilliseconds
    {
        get => _autoExposureMaxDurationMilliseconds;
        set
        {
            if (_autoExposureMaxDurationMilliseconds == value)
            {
                return;
            }

            _autoExposureMaxDurationMilliseconds = Math.Max(1, value);
            ApplyAutoExposureMaxDurationControl();
        }
    }

    public int AutoExposureMaxGain
    {
        get => _autoExposureMaxGain;
        set
        {
            if (_autoExposureMaxGain == value)
            {
                return;
            }

            _autoExposureMaxGain = Math.Max(0, value);
            ApplyAutoExposureMaxGainControl();
        }
    }

    public int AutoExposureBrightnessTarget
    {
        get => _autoExposureBrightnessTarget;
        set
        {
            if (_autoExposureBrightnessTarget == value)
            {
                return;
            }

            _autoExposureBrightnessTarget = Math.Max(0, value);
            ApplyAutoExposureBrightnessTargetControl();
        }
    }

    public int ExposureBrightness
    {
        get => _exposureBrightness;
        set
        {
            if (_exposureBrightness == value)
            {
                return;
            }

            _exposureBrightness = Math.Max(0, value);
            ApplyExposureBrightnessControl();
        }
    }

    public double MaxAttemptedFps
    {
        get => Volatile.Read(ref _maxAttemptedFps);
        set
        {
            var clamped = Math.Clamp(value, 0.1d, 30.0d);
            Volatile.Write(ref _maxAttemptedFps, clamped);
        }
    }

    public double ImageCircleRotationAngle
    {
        get => Volatile.Read(ref _imageCircleRotationAngle);
        set
        {
            Volatile.Write(ref _imageCircleRotationAngle, value);
        }
    }

    public void ClearLastImage()
    {
        lock (_stateSync)
        {
            _lastImageMetadata = (DateTimeOffset.MinValue, null);
        }

        var latestPath = Path.Combine(_cacheRoot, _options.LatestImageFileName);
        TryDeleteFile(latestPath);
    }

    public Task RunAsync(CancellationToken cancellationToken)
    {
        return RunInternalAsync(cancellationToken);
    }

    private async Task RunInternalAsync(CancellationToken cancellationToken)
    {
        InitializeCacheDirectory();

        _logger.LogInformation("AllSky camera service entering capture loop. Target cache root: {CacheRoot}", _cacheRoot);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ExecuteCameraLoopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (CameraUnavailableException ex)
            {
                _logger.LogWarning(ex, "Camera not available. Waiting {DelaySeconds}s before retry.", _options.RestartOnFailureWaitTimeSeconds);
                await DelayWithCancellationAsync(_options.RestartOnFailureWaitTimeSeconds, cancellationToken).ConfigureAwait(false);
            }
            catch (ASICameraException ex)
            {
                _logger.LogWarning(ex, "ASI SDK reported an error. Restarting loop in {DelaySeconds}s.", _options.RestartOnFailureWaitTimeSeconds);
                await DelayWithCancellationAsync(_options.RestartOnFailureWaitTimeSeconds, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected failure in camera loop. Restarting in {DelaySeconds}s.", _options.RestartOnFailureWaitTimeSeconds);
                await DelayWithCancellationAsync(_options.RestartOnFailureWaitTimeSeconds, cancellationToken).ConfigureAwait(false);
            }
        }

        _logger.LogInformation("AllSky camera service capture loop exiting.");
    }

    private Task ExecuteCameraLoopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ZwoCameraManager.Count <= _options.CameraIndex)
        {
            throw new CameraUnavailableException($"No ZWO camera detected at index {_options.CameraIndex}.");
        }

        using var camera = ZwoCameraManager.Create(_options.CameraIndex);
        camera.Open();

        _logger.LogInformation("Connected to camera {CameraName} (ID: {CameraId}) with maximum resolution {Width}x{Height}.", camera.Name, camera.CameraId, camera.MaxWidth, camera.MaxHeight);

        ConfigureCameraControls(camera);
        var captureArea = ConfigureCaptureArea(camera);

        using var buffer = new UnmanagedBuffer(captureArea.CalculateImageBufferSizeBytes());
        camera.StartVideoCapture();

    _isRecording = true;

        var frameThrottle = Stopwatch.StartNew();
        var frameInterval = Stopwatch.StartNew();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var exposureMicroseconds = GetExposureDurationMicroseconds();
                var waitTime = (int)Math.Max(50, exposureMicroseconds / 1000.0 * 1.2);
                var hasFrame = camera.TryGetVideoData(buffer.Pointer, buffer.Size, waitTime);

                if (!hasFrame)
                {
                    continue;
                }

                var requiredFrameSpacingMs = CalculateRequiredFrameSpacing();
                if (frameThrottle.Elapsed.TotalMilliseconds < requiredFrameSpacingMs)
                {
                    continue;
                }

                frameThrottle.Restart();

                using var bitmap = ConvertFrameToBitmap(buffer.Pointer, captureArea);

                var exposureDuration = TimeSpan.FromMilliseconds(exposureMicroseconds / 1000.0);
                var gainState = ReadControlState(_exposureGainControl, _exposureGain, _autoExposureGain);
                var brightnessState = ReadControlState(_exposureBrightnessControl, _exposureBrightness, _autoExposureBrightness);
                var fpsEstimate = CalculateFps(frameInterval);
                frameInterval.Restart();

                var metadata = new FrameMetadata(
                    Timestamp: DateTimeOffset.UtcNow,
                    ExposureDuration: exposureDuration,
                    Gain: gainState.Value,
                    AutoGain: gainState.Auto,
                    Brightness: brightnessState.Value,
                    AutoBrightness: brightnessState.Auto,
                    FramesPerSecondEstimate: fpsEstimate,
                    DroppedFrames: camera.GetDroppedFrames());

                try
                {
                    ProcessAndSaveFrame(bitmap, metadata);
                }
                catch (Exception processingError)
                {
                    _logger.LogError(processingError, "Failed to post-process camera frame.");
                }
            }
        }
        finally
        {
            camera.StopVideoCapture();
            ReleaseControlReferences();
            _isRecording = false;
            _logger.LogInformation("Camera capture loop stopped for {CameraName}.", camera.Name);
        }

        return Task.CompletedTask;
    }

    private void ConfigureCameraControls(ZwoCamera camera)
    {
        _exposureBrightnessControl = GetControl(camera, ASICamera2.ASI_CONTROL_TYPE.ASI_BRIGHTNESS);
        _exposureGainControl = GetControl(camera, ASICamera2.ASI_CONTROL_TYPE.ASI_GAIN);
        _exposureDurationControl = GetControl(camera, ASICamera2.ASI_CONTROL_TYPE.ASI_EXPOSURE);
        _autoExposureMaxGainControl = GetControl(camera, ASICamera2.ASI_CONTROL_TYPE.ASI_AUTO_MAX_GAIN);
    _autoExposureMaxDurationControl = GetControl(camera, ASICamera2.ASI_CONTROL_TYPE.ASI_AUTO_MAX_EXP);
    _autoExposureTargetBrightnessControl = GetControl(camera, ASICamera2.ASI_CONTROL_TYPE.ASI_AUTO_MAX_BRIGHTNESS);
    _bandwidthControl = GetControl(camera, ASICamera2.ASI_CONTROL_TYPE.ASI_BANDWIDTHOVERLOAD);

        if (_bandwidthControl is not null)
        {
            var bandwidthValue = Math.Clamp(75, _bandwidthControl.MinValue, _bandwidthControl.MaxValue);
            _bandwidthControl.SetValue(bandwidthValue, false);
        }


        ApplyExposureBrightnessControl();
        ApplyExposureGainControl();
        ApplyExposureDurationControl();
        ApplyAutoExposureMaxGainControl();
        ApplyAutoExposureMaxDurationControl();
        ApplyAutoExposureBrightnessTargetControl();
    }

    private CaptureAreaInfo ConfigureCaptureArea(ZwoCamera camera)
    {
        var desiredWidth = _options.CameraRoiWidth > 0 ? _options.CameraRoiWidth : camera.MaxWidth;
        var desiredHeight = _options.CameraRoiHeight > 0 ? _options.CameraRoiHeight : camera.MaxHeight;

        desiredWidth = AlignToMultiple(desiredWidth, 8);
        desiredHeight = AlignToMultiple(desiredHeight, 2);

        desiredWidth = Math.Min(desiredWidth, camera.MaxWidth);
        desiredHeight = Math.Min(desiredHeight, camera.MaxHeight);

        var startX = Math.Max(0, (camera.MaxWidth - desiredWidth) / 2);
        var startY = Math.Max(0, (camera.MaxHeight - desiredHeight) / 2);
        startX = AlignToMultiple(startX, 4);
        startY = AlignToMultiple(startY, 2);

        var captureArea = new CaptureAreaInfo(new Size(desiredWidth, desiredHeight), new Point(startX, startY), _options.ImageBinMode, _options.ImageType);

        camera.SetCaptureArea(captureArea);
        camera.SetStartPosition(captureArea.StartPosition);

        _logger.LogInformation("Configured ROI {Width}x{Height} (Bin {Bin}, Type {ImageType}) starting at {StartX},{StartY}.",
            captureArea.Size.Width,
            captureArea.Size.Height,
            captureArea.Bin,
            captureArea.ImageType,
            captureArea.StartPosition.X,
            captureArea.StartPosition.Y);

        return captureArea;
    }

    private void ProcessAndSaveFrame(Bitmap source, FrameMetadata metadata)
    {
        using var rotated = ApplyRotationIfNeeded(source);
        using var resized = ResizeImage(rotated, _options.ImageWidth, _options.ImageHeight);

        if (_options.UseImageCircleMask)
        {
            ApplyCircularMask(resized);
        }

        DrawOverlay(resized, metadata);
        SaveLatestImage(resized, metadata.Timestamp);
    }

    private void SaveLatestImage(Image image, DateTimeOffset timestamp)
    {
        var tempFilePath = Path.Combine(_cacheRoot, $"{timestamp:yyyyMMddHHmmssfff}.tmp");
        var finalPath = Path.Combine(_cacheRoot, _options.LatestImageFileName);

        try
        {
            Directory.CreateDirectory(_cacheRoot);

            using (var encoderParameters = CreateEncoderParameters())
            {
                image.Save(tempFilePath, _jpegEncoder, encoderParameters);
            }

            File.Move(tempFilePath, finalPath, true);

            lock (_stateSync)
            {
                _lastImageMetadata = (timestamp, _options.LatestImageFileName);
            }

            _logger.LogDebug("Captured AllSky frame saved to {FinalPath}", finalPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist AllSky frame to disk at {FinalPath}", finalPath);
            TryDeleteFile(tempFilePath);
        }
    }

    private void DrawOverlay(Image image, FrameMetadata metadata)
    {
        using var graphics = Graphics.FromImage(image);
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var localTimestamp = metadata.Timestamp.ToLocalTime();
        var timestampText = localTimestamp.ToString("yyyy-MM-dd HH:mm:ss zzz");
        var exposureText = $"Exposure: {metadata.ExposureDuration.TotalSeconds:F2}s  Gain: {metadata.Gain}{(metadata.AutoGain ? " (Auto)" : string.Empty)}";
        var brightnessText = $"Brightness: {metadata.Brightness}{(metadata.AutoBrightness ? " (Auto)" : string.Empty)}  FPS: {metadata.FramesPerSecondEstimate:F2}  Dropped: {metadata.DroppedFrames}";

        using var font = new Font(FontFamily.GenericSansSerif, (float)_options.ImageTextFontSize, FontStyle.Bold, GraphicsUnit.Point);
        using var shadowBrush = new SolidBrush(Color.FromArgb(180, Color.Black));
        using var textBrush = new SolidBrush(Color.White);

        var margin = 20f;
        var lineHeight = font.GetHeight(graphics) + 4f;

        DrawStringWithShadow(graphics, timestampText, font, textBrush, shadowBrush, new PointF(margin, margin));
        DrawStringWithShadow(graphics, exposureText, font, textBrush, shadowBrush, new PointF(margin, margin + lineHeight));
        DrawStringWithShadow(graphics, brightnessText, font, textBrush, shadowBrush, new PointF(margin, margin + (lineHeight * 2)));
    }

    private static void DrawStringWithShadow(Graphics graphics, string text, Font font, Brush mainBrush, Brush shadowBrush, PointF position)
    {
        var shadowOffset = new PointF(position.X + 2, position.Y + 2);
        graphics.DrawString(text, font, shadowBrush, shadowOffset);
        graphics.DrawString(text, font, mainBrush, position);
    }

    private static Bitmap ResizeImage(Image source, int width, int height)
    {
        var destination = new Bitmap(width, height);
        destination.SetResolution(source.HorizontalResolution, source.VerticalResolution);

        using var graphics = Graphics.FromImage(destination);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(Color.Black);

        var ratioX = (double)width / source.Width;
        var ratioY = (double)height / source.Height;
        var ratio = Math.Min(ratioX, ratioY);
        var scaledWidth = (int)(source.Width * ratio);
        var scaledHeight = (int)(source.Height * ratio);
        var offsetX = (width - scaledWidth) / 2;
        var offsetY = (height - scaledHeight) / 2;

        var destinationRect = new Rectangle(offsetX, offsetY, scaledWidth, scaledHeight);
        graphics.DrawImage(source, destinationRect, new Rectangle(0, 0, source.Width, source.Height), GraphicsUnit.Pixel);

        return destination;
    }

    private void ApplyCircularMask(Image image)
    {
        using var graphics = Graphics.FromImage(image);
        using var path = new GraphicsPath();

        var ellipseRect = new Rectangle(
            _options.ImageCircleOffsetX,
            _options.ImageCircleOffsetY,
            _options.ImageCircleDiameter,
            _options.ImageCircleDiameter);

        path.AddEllipse(ellipseRect);
        using var region = new Region(new Rectangle(0, 0, image.Width, image.Height));
        region.Exclude(path);

        using var brush = new SolidBrush(Color.Black);
        graphics.FillRegion(brush, region);
    }

    private Bitmap ApplyRotationIfNeeded(Bitmap source)
    {
        var angle = Volatile.Read(ref _imageCircleRotationAngle);
        if (Math.Abs(angle) < 0.01d)
        {
            return (Bitmap)source.Clone();
        }

        var rotated = new Bitmap(source.Width, source.Height);
        rotated.SetResolution(source.HorizontalResolution, source.VerticalResolution);

        using var graphics = Graphics.FromImage(rotated);
        graphics.TranslateTransform(source.Width / 2f, source.Height / 2f);
        graphics.RotateTransform((float)angle);
        graphics.TranslateTransform(-source.Width / 2f, -source.Height / 2f);
        graphics.DrawImage(source, new PointF(0, 0));

        return rotated;
    }

    private EncoderParameters CreateEncoderParameters()
    {
        var encoderParameters = new EncoderParameters(1);
        encoderParameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, _options.JpegImageQuality);
        return encoderParameters;
    }

    private double CalculateRequiredFrameSpacing()
    {
        var fps = Volatile.Read(ref _maxAttemptedFps);
        if (fps <= 0.01d)
        {
            return 0d;
        }

        return 1000d / fps;
    }

    private static double CalculateFps(Stopwatch interval)
    {
        var elapsed = interval.Elapsed.TotalMilliseconds;
        if (elapsed <= 0.001d)
        {
            return 0d;
        }

        return 1000d / elapsed;
    }

    private int GetExposureDurationMicroseconds()
    {
        if (_exposureDurationControl is null)
        {
            return _exposureDurationMilliseconds * 1000;
        }

        return _exposureDurationControl.Value;
    }

    private void ApplyExposureBrightnessControl()
    {
        if (_exposureBrightnessControl is null)
        {
            return;
        }

        _exposureBrightnessControl.SetValue(_exposureBrightness, _autoExposureBrightness);
    }

    private void ApplyExposureGainControl()
    {
        if (_exposureGainControl is null)
        {
            return;
        }

        _exposureGainControl.SetValue(_exposureGain, _autoExposureGain);

        if (_autoExposureMaxGainControl is not null)
        {
            _autoExposureMaxGainControl.SetValue(_autoExposureMaxGain, false);
        }
    }

    private void ApplyExposureDurationControl()
    {
        if (_exposureDurationControl is null)
        {
            return;
        }

        var targetValue = _autoExposureDuration
            ? _exposureDurationControl.Value
            : _exposureDurationMilliseconds * 1000;

        _exposureDurationControl.SetValue(targetValue, _autoExposureDuration);
        ApplyAutoExposureMaxDurationControl();
    }

    private void ApplyAutoExposureMaxDurationControl()
    {
        if (_autoExposureMaxDurationControl is null)
        {
            return;
        }

        var microseconds = _autoExposureMaxDurationMilliseconds * 1000;
        _autoExposureMaxDurationControl.SetValue(microseconds, false);
    }

    private void ApplyAutoExposureMaxGainControl()
    {
        if (_autoExposureMaxGainControl is null)
        {
            return;
        }

        _autoExposureMaxGainControl.SetValue(_autoExposureMaxGain, false);
    }

    private void ApplyAutoExposureBrightnessTargetControl()
    {
        if (_autoExposureTargetBrightnessControl is null)
        {
            return;
        }

        _autoExposureTargetBrightnessControl.SetValue(_autoExposureBrightnessTarget, false);
    }

    private static (int Value, bool Auto) ReadControlState(CameraControl? control, int fallbackValue, bool fallbackAuto)
    {
        if (control is null)
        {
            return (fallbackValue, fallbackAuto);
        }

        return (control.Value, control.IsAuto);
    }

    private void ReleaseControlReferences()
    {
        _exposureBrightnessControl = null;
        _exposureGainControl = null;
        _exposureDurationControl = null;
        _autoExposureMaxGainControl = null;
        _autoExposureMaxDurationControl = null;
        _autoExposureTargetBrightnessControl = null;
    _bandwidthControl = null;
    }

    private static CameraControl? GetControl(ZwoCamera camera, ASICamera2.ASI_CONTROL_TYPE controlType)
    {
        return camera.TryGetControl(controlType, out var control) ? control : null;
    }

    private void InitializeCacheDirectory()
    {
        try
        {
            if (_options.CleanCacheOnStart && Directory.Exists(_options.CacheImageSaveRoot))
            {
                Directory.Delete(_options.CacheImageSaveRoot, recursive: true);
            }

            Directory.CreateDirectory(_options.CacheImageSaveRoot);
            _cacheRoot = _options.CacheImageSaveRoot;
            _cacheFallbackLogged = false;
        }
        catch (Exception ex)
        {
            var fallbackPath = Path.Combine(Path.GetTempPath(), "hvo", "skymonitor-cache");
            try
            {
                Directory.CreateDirectory(fallbackPath);
                _cacheRoot = fallbackPath;
                if (!_cacheFallbackLogged)
                {
                    _logger.LogWarning(ex, "Failed to initialize cache directory at {PrimaryPath}. Using fallback {FallbackPath}.", _options.CacheImageSaveRoot, fallbackPath);
                    _cacheFallbackLogged = true;
                }
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError(fallbackEx, "Failed to initialize fallback cache directory at {FallbackPath}.", fallbackPath);
                throw;
            }
        }
    }

    private static int AlignToMultiple(int value, int multiple)
    {
        if (multiple <= 1)
        {
            return value;
        }

        return (value / multiple) * multiple;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private static async Task DelayWithCancellationAsync(uint delaySeconds, CancellationToken cancellationToken)
    {
        if (delaySeconds == 0)
        {
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation requested, just exit.
        }
    }

    private static unsafe Bitmap ConvertFrameToBitmap(IntPtr buffer, CaptureAreaInfo captureArea)
    {
        return captureArea.ImageType switch
        {
            ASICamera2.ASI_IMG_TYPE.ASI_IMG_RAW16 => ConvertRaw16ToBitmap((ushort*)buffer.ToPointer(), captureArea.Size.Width, captureArea.Size.Height),
            ASICamera2.ASI_IMG_TYPE.ASI_IMG_RGB24 => ConvertRgb24ToBitmap((byte*)buffer.ToPointer(), captureArea.Size.Width, captureArea.Size.Height),
            _ => ConvertRaw8ToBitmap((byte*)buffer.ToPointer(), captureArea.Size.Width, captureArea.Size.Height)
        };
    }

    private static unsafe Bitmap ConvertRaw16ToBitmap(ushort* source, int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

        try
        {
            for (var y = 0; y < height; y++)
            {
                var srcRow = source + (y * width);
                var destRow = (byte*)data.Scan0 + (y * data.Stride);

                for (var x = 0; x < width; x++)
                {
                    var value16 = srcRow[x];
                    var value8 = (byte)(value16 >> 8);
                    destRow[(x * 3) + 0] = value8;
                    destRow[(x * 3) + 1] = value8;
                    destRow[(x * 3) + 2] = value8;
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    private static unsafe Bitmap ConvertRaw8ToBitmap(byte* source, int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

        try
        {
            for (var y = 0; y < height; y++)
            {
                var srcRow = source + (y * width);
                var destRow = (byte*)data.Scan0 + (y * data.Stride);

                for (var x = 0; x < width; x++)
                {
                    var value = srcRow[x];
                    destRow[(x * 3) + 0] = value;
                    destRow[(x * 3) + 1] = value;
                    destRow[(x * 3) + 2] = value;
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    private static unsafe Bitmap ConvertRgb24ToBitmap(byte* source, int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

        try
        {
            for (var y = 0; y < height; y++)
            {
                var srcRow = source + (y * width * 3);
                var destRow = (byte*)data.Scan0 + (y * data.Stride);

                for (var x = 0; x < width; x++)
                {
                    var srcIndex = x * 3;
                    var r = srcRow[srcIndex + 0];
                    var g = srcRow[srcIndex + 1];
                    var b = srcRow[srcIndex + 2];

                    destRow[(x * 3) + 0] = b;
                    destRow[(x * 3) + 1] = g;
                    destRow[(x * 3) + 2] = r;
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    private readonly record struct FrameMetadata(
        DateTimeOffset Timestamp,
        TimeSpan ExposureDuration,
        int Gain,
        bool AutoGain,
        int Brightness,
        bool AutoBrightness,
        double FramesPerSecondEstimate,
        int DroppedFrames);

    private sealed class CameraUnavailableException : Exception
    {
        public CameraUnavailableException(string message) : base(message)
        {
        }
    }

    private sealed class UnmanagedBuffer : IDisposable
    {
        public UnmanagedBuffer(long size)
        {
            if (size <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            Size = size;
            Pointer = Marshal.AllocHGlobal(new IntPtr(size));
            GC.AddMemoryPressure(size);
        }

        public IntPtr Pointer { get; }
        public long Size { get; }

        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (Pointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(Pointer);
            }

            GC.RemoveMemoryPressure(Size);
            _disposed = true;
        }
    }
}
#pragma warning restore CA1416
