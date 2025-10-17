#nullable enable

using System;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Cameras.Zwo;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Services;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Tests.Benchmarking;

[Config(typeof(Config))]
public class SkyMonitorBenchmarks
{
    private ProjectionMath.ProjectionBasis _projectionBasis;
    private ExposureAccumulatorFixture? _exposureFixture;
    private PixelConversionFixture? _pixelFixture;
    private SKBitmap? _rgb24Bitmap;
    private SKBitmap? _grayBitmap;

    [Params(256, 512)]
    public int Width { get; set; }

    [Params(256, 512)]
    public int Height { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _projectionBasis = ProjectionMath.BuildBasis(alt0Deg: 35.0, az0Deg: 110.0);

        _exposureFixture = new ExposureAccumulatorFixture(Width, Height);
        _rgb24Bitmap = _exposureFixture.CreateBitmap(SKColorType.Bgra8888);
        _grayBitmap = _exposureFixture.CreateBitmap(SKColorType.Gray8);

        _pixelFixture = new PixelConversionFixture(Width, Height);
        _pixelFixture.SeedBuffers();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _grayBitmap?.Dispose();
        _rgb24Bitmap?.Dispose();
        _exposureFixture?.Dispose();
        _pixelFixture?.Dispose();
    }

    [Benchmark]
    public ProjectionMath.ProjectionVector ComputeProjectionDirection()
    {
        var alt = 45.0;
        var az = 180.0;
        var direction = ProjectionMath.DirFromAltAz(alt, az);

        // Dot against basis to exercise math path.
        _ = ProjectionMath.Dot(direction, _projectionBasis.B);
        _ = ProjectionMath.Dot(direction, _projectionBasis.E1);
        _ = ProjectionMath.Dot(direction, _projectionBasis.E2);
        return direction;
    }

    [Benchmark]
    public ExposureMetrics ExposureAccumulator_Gray()
        => ExposureAccumulator.ComputeMetrics(_grayBitmap!);

    [Benchmark]
    public ExposureMetrics ExposureAccumulator_Rgb()
        => ExposureAccumulator.ComputeMetrics(_rgb24Bitmap!);

    [Benchmark]
    public int PixelConverter_Rgb24ToBgra()
    {
        var bitmap = ZwoPixelConverter.CreateBgraBitmapFromRgb24(_pixelFixture!.Rgb24Pointer, Width, Height, _pixelFixture.Rgb24RowBytes);
        var length = bitmap.GetPixelSpan().Length;
        bitmap.Dispose();
        return length;
    }

    [Benchmark]
    public int PixelConverter_Raw16ToGray()
    {
        var bitmap = ZwoPixelConverter.CreateGrayBitmapFromRaw16(_pixelFixture!.Raw16Pointer, Width, Height, _pixelFixture.Raw16RowBytes);
        var length = bitmap.GetPixelSpan().Length;
        bitmap.Dispose();
        return length;
    }

    [Benchmark]
    public int PixelConverter_Y8ToGray()
    {
        var bitmap = ZwoPixelConverter.CreateGrayBitmapFromY8(_pixelFixture!.Y8Pointer, Width, Height, _pixelFixture.Y8RowBytes);
        var length = bitmap.GetPixelSpan().Length;
        bitmap.Dispose();
        return length;
    }

    public class Config : ManualConfig
    {
        public Config()
        {
            AddJob(Job.Default.WithId("Default"));
        }
    }
}

public static class SkyMonitorBenchmarkRunner
{
    public static void Run()
    {
        BenchmarkRunner.Run<SkyMonitorBenchmarks>();
    }
}

public sealed class ExposureAccumulatorFixture : IDisposable
{
    private readonly Random _random = new(42);
    private readonly int _width;
    private readonly int _height;

    public ExposureAccumulatorFixture(int width, int height)
    {
        _width = width;
        _height = height;
    }

    public SKBitmap CreateBitmap(SKColorType colorType)
    {
        var info = new SKImageInfo(_width, _height, colorType, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        var span = bitmap.GetPixelSpan();
        _random.NextBytes(span);
        return bitmap;
    }

    public void Dispose()
    {
        // Nothing to dispose; bitmaps are disposed by the caller.
    }
}

public sealed class PixelConversionFixture : IDisposable
{
    private readonly int _width;
    private readonly int _height;
    private GCHandle _rgb24Handle;
    private GCHandle _raw16Handle;
    private GCHandle _y8Handle;

    public PixelConversionFixture(int width, int height)
    {
        _width = width;
        _height = height;

        Rgb24Buffer = new byte[width * height * 3];
        Raw16Buffer = new byte[width * height * 2];
        Y8Buffer = new byte[width * height];

        _rgb24Handle = GCHandle.Alloc(Rgb24Buffer, GCHandleType.Pinned);
        _raw16Handle = GCHandle.Alloc(Raw16Buffer, GCHandleType.Pinned);
        _y8Handle = GCHandle.Alloc(Y8Buffer, GCHandleType.Pinned);
    }

    public byte[] Rgb24Buffer { get; }
    public byte[] Raw16Buffer { get; }
    public byte[] Y8Buffer { get; }

    public int Rgb24RowBytes => _width * 3;
    public int Raw16RowBytes => _width * 2;
    public int Y8RowBytes => _width;

    public IntPtr Rgb24Pointer => _rgb24Handle.AddrOfPinnedObject();
    public IntPtr Raw16Pointer => _raw16Handle.AddrOfPinnedObject();
    public IntPtr Y8Pointer => _y8Handle.AddrOfPinnedObject();

    public void SeedBuffers()
    {
        var random = new Random(Seed: 1234);
        random.NextBytes(Rgb24Buffer);
        random.NextBytes(Raw16Buffer);
        random.NextBytes(Y8Buffer);
    }

    public void Dispose()
    {
        if (_rgb24Handle.IsAllocated)
        {
            _rgb24Handle.Free();
        }

        if (_raw16Handle.IsAllocated)
        {
            _raw16Handle.Free();
        }

        if (_y8Handle.IsAllocated)
        {
            _y8Handle.Free();
        }
    }
}
