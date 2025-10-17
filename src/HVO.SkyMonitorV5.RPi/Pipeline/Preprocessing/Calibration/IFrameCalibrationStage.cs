#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Cameras;
using HVO.SkyMonitorV5.RPi.Skia;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Pipeline.Preprocessing.Calibration;

/// <summary>
/// Represents a single preprocessing stage that can mutate frame pixels prior to stacking.
/// </summary>
public interface IFrameCalibrationStage
{
    string Name { get; }

    ValueTask ApplyAsync(FrameCalibrationContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Provides frame pixel buffers and metadata to calibration stages.
/// </summary>
public sealed class FrameCalibrationContext
{
    private readonly CameraAdapterBase.AdapterFrame _frame;
    private readonly SkiaSurfaceLease _surfaceLease;
    private float[]? _scratchBuffer;

    internal FrameCalibrationContext(CameraAdapterBase.AdapterFrame frame, SkiaSurfaceLease surfaceLease)
    {
        _frame = frame;
        _surfaceLease = surfaceLease;
    }

    public CameraAdapterBase.AdapterFrame Frame => _frame;

    public SkiaSurfaceLease SurfaceLease => _surfaceLease;

    public SKSurface Surface => _surfaceLease.Surface;

    /// <summary>
    /// Retrieves a scratch buffer of at least the requested length. Buffer contents are undefined.
    /// </summary>
    public Span<float> GetScratchBuffer(int minimumLength = 256)
    {
        if (minimumLength <= 0)
        {
            minimumLength = 1;
        }

        if (_scratchBuffer is null || _scratchBuffer.Length < minimumLength)
        {
            _scratchBuffer = new float[Math.Max(minimumLength, 256)];
        }

        return _scratchBuffer.AsSpan(0, minimumLength);
    }
}

/// <summary>
/// Factory abstraction that emits an ordered collection of calibration stages.
/// </summary>
public interface IFrameCalibrationPipelineFactory
{
    IFrameCalibrationStage[] BuildStages();
}

/// <summary>
/// Default factory that returns an empty pipeline. Consumers can register replacements via DI.
/// </summary>
public sealed class NullFrameCalibrationPipelineFactory : IFrameCalibrationPipelineFactory
{
    public static NullFrameCalibrationPipelineFactory Instance { get; } = new();

    private NullFrameCalibrationPipelineFactory()
    {
    }

    public IFrameCalibrationStage[] BuildStages() => Array.Empty<IFrameCalibrationStage>();
}

// TODO(dotnet10): introduce SIMD-aware calibration stages (dark subtraction, flat field) using future math helpers.
