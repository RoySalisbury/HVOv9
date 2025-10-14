using System;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Skia;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Exports;

/// <summary>
/// Helper responsible for building export envelopes and handing them to the dispatcher.
/// </summary>
public sealed class FrameExportPublisher
{
    private const string LegacyRawContentType = "image/png";
    private const string LegacyRawExtension = "png";

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
        SKImage? temporary = null;

        try
        {
            var sourceImage = capture.ImmutableImage;
            var imageForExport = sourceImage ?? (temporary = SKImage.FromBitmap(capture.Image));

            if (imageForExport is null)
            {
                _logger.LogWarning(
                    "Raw frame export skipped; no immutable image available for frame #{FrameNumber} ({FrameId}).",
                    frameNumber,
                    capture.FrameId);
                return;
            }

            if (SkiaRawFrameHelper.TryCreateRawPayload(imageForExport, out var rawBytes, out var descriptor))
            {
                var metadata = FrameExportMetadataBuilder.FromRaw(
                    capture,
                    rig,
                    stageTimestampUtc,
                    queueLatencyMilliseconds: captureMilliseconds,
                    processingMilliseconds: null,
                    rawImageDescriptor: descriptor);

                var envelope = new FrameExportEnvelope(
                    capture.FrameId,
                    FrameExportStage.Raw,
                    metadata,
                    new ReadOnlyMemory<byte>(rawBytes),
                    SkiaRawFrameHelper.RawContentType,
                    SkiaRawFrameHelper.RawFileExtension);

                if (!_dispatcher.TryEnqueue(envelope))
                {
                    _logger.LogDebug(
                        "Frame export dispatcher rejected raw payload for frame #{FrameNumber} ({FrameId}).",
                        frameNumber,
                        capture.FrameId);
                }
                return;
            }

            _logger.LogWarning(
                "Falling back to PNG encoding for raw frame #{FrameNumber} ({FrameId}) because pixel data could not be materialized without conversion.",
                frameNumber,
                capture.FrameId);

            using var encoded = imageForExport.Encode(SKEncodedImageFormat.Png, quality: 95);
            if (encoded is null)
            {
                _logger.LogWarning(
                    "Raw frame encode failed; skipping export for frame #{FrameNumber} ({FrameId}).",
                    frameNumber,
                    capture.FrameId);
                return;
            }

            var payload = encoded.ToArray();
            var fallbackMetadata = FrameExportMetadataBuilder.FromRaw(
                capture,
                rig,
                stageTimestampUtc,
                queueLatencyMilliseconds: captureMilliseconds,
                processingMilliseconds: null,
                rawImageDescriptor: null);

            var fallbackEnvelope = new FrameExportEnvelope(
                capture.FrameId,
                FrameExportStage.Raw,
                fallbackMetadata,
                payload,
                LegacyRawContentType,
                LegacyRawExtension);

            if (!_dispatcher.TryEnqueue(fallbackEnvelope))
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
        finally
        {
            temporary?.Dispose();
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
