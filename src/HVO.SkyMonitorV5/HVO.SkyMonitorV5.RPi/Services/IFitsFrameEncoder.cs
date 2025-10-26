using System;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Pipeline;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Services;

/// <summary>
/// Encodes frames into FITS payloads using CFITSIO, with optional compression and astro metadata.
/// </summary>
public interface IFitsFrameEncoder
{
  /// <summary>
  /// Encode a raw captured frame to FITS bytes using unified encoding options.
  /// </summary>
  /// <param name="bitmap">The raw frame bitmap to encode.</param>
  /// <param name="capture">Captured image metadata.</param>
  /// <param name="rig">Rig specification for headers.</param>
  /// <param name="options">Unified FITS encoding options (nullable for defaults).</param>
  ProcessedFrameDelivery EncodeRaw(SKBitmap bitmap, CapturedImage capture, RigSpec rig, FitsEncodingOptions? options);

  /// <summary>
  /// Encode a processed frame to FITS bytes using unified encoding options.
  /// </summary>
  /// <param name="frame">The processed frame to encode.</param>
  /// <param name="rig">Rig specification for headers.</param>
  /// <param name="options">Unified FITS encoding options (nullable for defaults).</param>
  ProcessedFrameDelivery EncodeProcessed(ProcessedFrame frame, RigSpec rig, FitsEncodingOptions? options);
}
