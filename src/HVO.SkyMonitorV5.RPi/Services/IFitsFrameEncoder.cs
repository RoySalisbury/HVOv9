using System;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Services;

/// <summary>
/// Encodes frames into FITS payloads using CFITSIO, with optional compression and astro metadata.
/// </summary>
public interface IFitsFrameEncoder
{
    /// <summary>Encode a raw frame (from SKImage) to FITS bytes.</summary>
    ProcessedFrameDelivery EncodeRaw(SKImage image, RawFrameSnapshot frame, RigSpec rig, FitsExportOptions options);

    /// <summary>Encode a processed frame to FITS bytes.</summary>
    ProcessedFrameDelivery EncodeProcessed(ProcessedFrame frame, RigSpec rig, FitsExportOptions options);
}
