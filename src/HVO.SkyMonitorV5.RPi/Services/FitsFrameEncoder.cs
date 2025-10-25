using System;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Services;

/// <summary>
/// Placeholder FITS encoder. Implementation will be added in Phase 2.
/// </summary>
public sealed class FitsFrameEncoder : IFitsFrameEncoder
{
    private readonly ILogger<FitsFrameEncoder> _logger;

    public FitsFrameEncoder(ILogger<FitsFrameEncoder> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ProcessedFrameDelivery EncodeRaw(SKImage image, RawFrameSnapshot frame, RigSpec rig, FitsExportOptions options)
    {
        // Phase 2 will implement FITS encoding using HVO.Astronomy.CFITSIO.
        throw new NotImplementedException("FITS raw encoding not yet implemented (Phase 2).");
    }

    public ProcessedFrameDelivery EncodeProcessed(ProcessedFrame frame, RigSpec rig, FitsExportOptions options)
    {
        // Phase 2 will implement FITS encoding using HVO.Astronomy.CFITSIO.
        throw new NotImplementedException("FITS processed encoding not yet implemented (Phase 2).");
    }
}
