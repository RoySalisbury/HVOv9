#nullable enable
using System;
using HVO.SkyMonitorV5.RPi.Options;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Services.RemoteDispatch;

public sealed class SkiaRemoteFrameEncoder : IRemoteFrameEncoder
{
    private readonly ILogger<SkiaRemoteFrameEncoder> _logger;

    public SkiaRemoteFrameEncoder(ILogger<SkiaRemoteFrameEncoder> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public RemoteFramePayload Encode(RemoteFrameEnvelope envelope, RemoteDispatchOptions options)
    {
        if (envelope is null)
        {
            throw new ArgumentNullException(nameof(envelope));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var image = envelope.CapturedFrame.Image;
        if (image is null)
        {
            throw new InvalidOperationException("Remote frame encoder received an envelope without an image.");
        }

        var (format, contentType, extension, quality) = ResolveEncoding(options.ImageFormat);

        using var encoded = image.Encode(format, quality);
        if (encoded is null)
        {
            throw new InvalidOperationException($"Failed to encode frame using {format}.");
        }

        return new RemoteFramePayload(encoded.ToArray(), contentType, extension);
    }

    private (SKEncodedImageFormat Format, string ContentType, string Extension, int Quality) ResolveEncoding(RemoteDispatchImageFormat format)
    {
        return format switch
        {
            RemoteDispatchImageFormat.Jpeg => (SKEncodedImageFormat.Jpeg, "image/jpeg", "jpg", 90),
            RemoteDispatchImageFormat.Bmp => (SKEncodedImageFormat.Bmp, "image/bmp", "bmp", 100),
            RemoteDispatchImageFormat.Tiff => throw new NotSupportedException("TIFF encoding is not yet supported for remote dispatch."),
            RemoteDispatchImageFormat.Fits => throw new NotSupportedException("FITS encoding is not yet supported for remote dispatch."),
            _ => (SKEncodedImageFormat.Png, "image/png", "png", 95)
        };
    }
}
