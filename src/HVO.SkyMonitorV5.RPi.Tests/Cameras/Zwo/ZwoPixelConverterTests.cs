#nullable enable

using System;
using System.Runtime.InteropServices;
using HVO.SkyMonitorV5.RPi.Cameras.Zwo;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Tests.Cameras.Zwo;

[TestClass]
public sealed class ZwoPixelConverterTests
{
    [TestMethod]
    public void CreateBgraBitmapFromRgb24_ConvertsChannels()
    {
        const int width = 2;
        const int height = 1;
        var captureRowBytes = width * 3;
        var buffer = new byte[]
        {
            10, 20, 30,
            40, 50, 60
        };

        using var bitmap = ExecutePinned(buffer, pointer => ZwoPixelConverter.CreateBgraBitmapFromRgb24(pointer, width, height, captureRowBytes));

    Assert.AreEqual(new SKColor(10, 20, 30, 255), bitmap.GetPixel(0, 0));
    Assert.AreEqual(new SKColor(40, 50, 60, 255), bitmap.GetPixel(1, 0));
    }

    [TestMethod]
    public void CreateGrayBitmapFromRaw16_HighByteIsUsed()
    {
        const int width = 2;
        const int height = 1;
        var captureRowBytes = width * 2;
        var buffer = new byte[]
        {
            0, 255, // -> 255
            0, 128  // -> 128
        };

        using var bitmap = ExecutePinned(buffer, pointer => ZwoPixelConverter.CreateGrayBitmapFromRaw16(pointer, width, height, captureRowBytes));

        Assert.AreEqual(255, bitmap.GetPixelSpan()[0]);
        Assert.AreEqual(128, bitmap.GetPixelSpan()[1]);
    }

    [TestMethod]
    public void CreateGrayBitmapFromY8_PreservesValues()
    {
        const int width = 2;
        const int height = 2;
        var captureRowBytes = width;
        var buffer = new byte[]
        {
            10, 20,
            30, 40
        };

        using var bitmap = ExecutePinned(buffer, pointer => ZwoPixelConverter.CreateGrayBitmapFromY8(pointer, width, height, captureRowBytes));
        var pixels = bitmap.GetPixelSpan();

        CollectionAssert.AreEqual(buffer, pixels.ToArray());
    }

    private static SKBitmap ExecutePinned(byte[] source, Func<IntPtr, SKBitmap> factory)
    {
        var handle = GCHandle.Alloc(source, GCHandleType.Pinned);
        try
        {
            return factory(handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }
    }
}
