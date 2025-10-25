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

    public ProcessedFrameDelivery Encode(ProcessedFrame frame)
    {
        if (frame is null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        if (frame.ImmutableImage is null)
        {
            throw new InvalidOperationException("Processed frame does not contain an immutable SKImage for encoding.");
        }

        var fits = _fitsOptions.CurrentValue;
        if (fits.EnableForProcessed)
        {
            try
            {
                var rig = _rigAdapter.ActiveRig;
                var delivery = _fitsEncoder.EncodeProcessed(frame, rig, fits);
                // Force content type and extension for FITS
                return new ProcessedFrameDelivery(delivery.Payload, "application/fits", delivery.FileExtension ?? "fits");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FITS encoding for processed frame {FrameId} failed; falling back to configured image encoding.", frame.FrameId);
                // Fall through to legacy encoding
            }
        }

    var encoding = ImageEncodingUtilities.Normalize(frame.Encoding);
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
