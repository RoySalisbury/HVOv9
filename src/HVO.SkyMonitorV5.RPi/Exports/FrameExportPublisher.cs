using System;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Pipeline;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Exports;

/// <summary>
/// Helper responsible for building export envelopes and handing them to the dispatcher.
/// </summary>
public sealed class FrameExportPublisher
{
    private readonly IFrameExportDispatcher _dispatcher;
    private readonly ILogger<FrameExportPublisher> _logger;

    public FrameExportPublisher(IFrameExportDispatcher dispatcher, ILogger<FrameExportPublisher> logger)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void PublishRawFrame(
        int frameNumber,
        CapturedImage capture,
        RigSpec rig,
        double? captureMilliseconds,
        DateTimeOffset stageTimestampUtc)
    {
        try
        {
            using var image = SKImage.FromBitmap(capture.Image);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, quality: 95);
            if (encoded is null)
            {
                _logger.LogWarning(
                    "Raw frame encode failed; skipping export for frame #{FrameNumber} ({FrameId}).",
                    frameNumber,
                    capture.FrameId);
                return;
            }

            var payload = encoded.ToArray();
            var metadata = FrameExportMetadataBuilder.FromRaw(
                capture,
                rig,
                stageTimestampUtc,
                queueLatencyMilliseconds: captureMilliseconds,
                processingMilliseconds: null);

            var envelope = new FrameExportEnvelope(
                capture.FrameId,
                FrameExportStage.Raw,
                metadata,
                payload,
                "image/png",
                "png");

            if (!_dispatcher.TryEnqueue(envelope))
            {
                _logger.LogDebug(
                    "Frame export dispatcher rejected raw payload for frame #{FrameNumber} ({FrameId}).",
                    frameNumber,
                    capture.FrameId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to prepare raw frame export for frame #{FrameNumber} ({FrameId}).",
                frameNumber,
                capture.FrameId);
        }
    }

    public void PublishProcessedFrame(
        int frameNumber,
        FrameStackResult stackResult,
        ProcessedFrame processedFrame,
        RigSpec rig,
        double? queueLatencyMilliseconds,
        double? processingMilliseconds,
        DateTimeOffset stageTimestampUtc)
    {
        try
        {
            var metadata = FrameExportMetadataBuilder.FromProcessed(
                processedFrame,
                stackResult.Context,
                rig,
                stageTimestampUtc,
                queueLatencyMilliseconds,
                processingMilliseconds);

            var payload = new ReadOnlyMemory<byte>(processedFrame.ImageBytes);
            var envelope = new FrameExportEnvelope(
                processedFrame.FrameId,
                FrameExportStage.Processed,
                metadata,
                payload,
                processedFrame.ContentType,
                TryGetFileExtension(processedFrame.ContentType));

            if (!_dispatcher.TryEnqueue(envelope))
            {
                _logger.LogDebug(
                    "Frame export dispatcher rejected processed payload for frame #{FrameNumber} ({FrameId}).",
                    frameNumber,
                    processedFrame.FrameId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to prepare processed frame export for frame #{FrameNumber} ({FrameId}).",
                frameNumber,
                processedFrame.FrameId);
        }
    }

    private static string? TryGetFileExtension(string contentType) => contentType switch
    {
        "image/png" => "png",
        "image/jpeg" => "jpg",
        _ => null
    };
}
