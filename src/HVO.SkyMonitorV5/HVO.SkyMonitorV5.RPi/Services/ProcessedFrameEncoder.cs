using System;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Cameras.Acquisition;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Services;

/// <summary>
/// Default encoder that materializes processed frames into the configured delivery format.
/// </summary>
public sealed class ProcessedFrameEncoder : IProcessedFrameEncoder
{
    private readonly ILogger<ProcessedFrameEncoder> _logger;
    private readonly IFitsFrameEncoder _fitsEncoder;
    private readonly IRigAcquisitionAdapter _rigAdapter;
    private readonly IOptionsMonitor<FitsExportOptions> _fitsOptions;

    public ProcessedFrameEncoder(
        ILogger<ProcessedFrameEncoder> logger,
        IFitsFrameEncoder fitsEncoder,
        IRigAcquisitionAdapter rigAdapter,
        IOptionsMonitor<FitsExportOptions> fitsOptions)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _fitsEncoder = fitsEncoder ?? throw new ArgumentNullException(nameof(fitsEncoder));
        _rigAdapter = rigAdapter ?? throw new ArgumentNullException(nameof(rigAdapter));
        _fitsOptions = fitsOptions ?? throw new ArgumentNullException(nameof(fitsOptions));
    }

    public ProcessedFrameDelivery Encode(
        ProcessedFrame frame,
        ProcessedFrameEncodingContext context = ProcessedFrameEncodingContext.UserInterface,
        ImageEncodingSettings? customEncoding = null)
    {
        if (frame is null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        if (frame.ImmutableImage is null)
        {
            throw new InvalidOperationException("Processed frame does not contain an immutable SKImage for encoding.");
        }

        // Use custom encoding if provided, otherwise fall back to frame's encoding settings
        var encoding = customEncoding is not null
            ? ImageEncodingUtilities.Normalize(customEncoding)
            : ImageEncodingUtilities.Normalize(frame.Encoding);

        // If explicitly asked to encode FITS, use unified encoder path
        if (encoding.Format == ImageEncodingFormat.Fits)
        {
            try
            {
                var rig = _rigAdapter.ActiveRig;
                var delivery = _fitsEncoder.EncodeProcessed(frame, rig, encoding.FitsOptions);
                return new ProcessedFrameDelivery(delivery.Payload, "application/fits", delivery.FileExtension ?? "fits");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FITS encoding (unified) for processed frame {FrameId} failed; falling back to raster encoding.", frame.FrameId);
                // Fall through to raster
            }
        }

        // Legacy behavior: when no explicit encoding provided and export context requests FITS via legacy options
        var fits = _fitsOptions.CurrentValue;
        if (customEncoding is null && context == ProcessedFrameEncodingContext.Export && fits.EnableForProcessed)
        {
            try
            {
                var rig = _rigAdapter.ActiveRig;
                var delivery = _fitsEncoder.EncodeProcessed(frame, rig, fits);
                return new ProcessedFrameDelivery(delivery.Payload, "application/fits", delivery.FileExtension ?? "fits");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FITS encoding (legacy) for processed frame {FrameId} failed; falling back to raster encoding.", frame.FrameId);
                // Fall through to raster
            }
        }

        // Raster path: encode via Skia using requested format/quality
        var format = ImageEncodingUtilities.ToSkiaFormat(encoding.Format);

        using var data = frame.ImmutableImage.Encode(format, encoding.Quality);
        if (data is null)
        {
            _logger.LogWarning(
                "Failed to encode processed frame {FrameId} using format {Format}.",
                frame.FrameId,
                encoding.Format);
            throw new InvalidOperationException(FormattableString.Invariant($"Failed to encode processed frame {frame.FrameId}."));
        }

        var bytes = data.ToArray();
        var contentType = ImageEncodingUtilities.ToContentType(encoding.Format);
        var extension = ImageEncodingUtilities.ToFileExtension(encoding.Format);

        return new ProcessedFrameDelivery(bytes, contentType, extension);
    }
}
