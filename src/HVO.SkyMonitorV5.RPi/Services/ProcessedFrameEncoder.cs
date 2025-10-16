using System;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Pipeline;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Services;

/// <summary>
/// Default encoder that materializes processed frames into the configured delivery format.
/// </summary>
public sealed class ProcessedFrameEncoder : IProcessedFrameEncoder
{
    private readonly ILogger<ProcessedFrameEncoder> _logger;

    public ProcessedFrameEncoder(ILogger<ProcessedFrameEncoder> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
