using System;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Services;
using HVO.SkyMonitorV5.RPi.Skia;
using HVO.SkyMonitorV5.RPi.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    private readonly IProcessedFrameEncoder _processedFrameEncoder;
    private readonly ILogger<FrameExportPublisher> _logger;
    private readonly IOptionsMonitor<SkiaPipelineFeatureOptions> _featureOptions;
    private readonly ISkiaPipelineFeatureToggleMonitor _featureMonitor;

    public FrameExportPublisher(
        IFrameExportDispatcher dispatcher,
        IProcessedFrameEncoder processedFrameEncoder,
        ILogger<FrameExportPublisher> logger,
        IOptionsMonitor<SkiaPipelineFeatureOptions> featureOptions,
        ISkiaPipelineFeatureToggleMonitor featureMonitor)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _processedFrameEncoder = processedFrameEncoder ?? throw new ArgumentNullException(nameof(processedFrameEncoder));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _featureOptions = featureOptions ?? throw new ArgumentNullException(nameof(featureOptions));
        _featureMonitor = featureMonitor ?? throw new ArgumentNullException(nameof(featureMonitor));
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

            var features = _featureOptions.CurrentValue;
            var allowRawLinearPayloads = features.EnableRawLinearPayloads;

            if (allowRawLinearPayloads && SkiaRawFrameHelper.TryCreateRawPayload(imageForExport, out var rawBytes, out var descriptor))
            {
                var metadata = FrameExportMetadataBuilder.FromRaw(
                    capture,
                    rig,
                    stageTimestampUtc,
                    queueLatencyMilliseconds: captureMilliseconds,
                    processingMilliseconds: null,
                    rawImageDescriptor: descriptor,
                    payloadContentType: SkiaRawFrameHelper.RawContentType,
                    payloadExtension: SkiaRawFrameHelper.RawFileExtension);

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

            if (!allowRawLinearPayloads)
            {
                _featureMonitor.RecordFallback(SkiaPipelineFeatureNames.RawLinearPayloads);
                _logger.LogDebug(
                    "Raw frame exports using PNG fallback because feature '{FeatureName}' is disabled.",
                    nameof(features.EnableRawLinearPayloads));
            }
            else
            {
                _logger.LogWarning(
                    "Falling back to PNG encoding for raw frame #{FrameNumber} ({FrameId}) because pixel data could not be materialized without conversion.",
                    frameNumber,
                    capture.FrameId);
            }

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
                rawImageDescriptor: null,
                payloadContentType: LegacyRawContentType,
                payloadExtension: LegacyRawExtension);

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
            var features = _featureOptions.CurrentValue;

            if (features.EnableProcessedFrameEncoder)
            {
                var delivery = _processedFrameEncoder.Encode(processedFrame);
                var contentType = delivery.ContentType;
                var fileExtension = delivery.FileExtension ?? processedFrame.FileExtension ?? TryGetFileExtension(contentType);

                var metadata = FrameExportMetadataBuilder.FromProcessed(
                    processedFrame,
                    stackResult.Context,
                    rig,
                    stageTimestampUtc,
                    queueLatencyMilliseconds,
                    processingMilliseconds,
                    payloadContentType: contentType,
                    payloadExtension: fileExtension);

                var payload = delivery.Payload;
                var envelope = new FrameExportEnvelope(
                    processedFrame.FrameId,
                    FrameExportStage.Processed,
                    metadata,
                    payload,
                    contentType,
                    fileExtension);

                if (!_dispatcher.TryEnqueue(envelope))
                {
                    _logger.LogDebug(
                        "Frame export dispatcher rejected processed payload for frame #{FrameNumber} ({FrameId}).",
                        frameNumber,
                        processedFrame.FrameId);
                }
                return;
            }

            _featureMonitor.RecordFallback(SkiaPipelineFeatureNames.ProcessedFrameEncoder);
            _logger.LogDebug(
                "Processed frame exports using fallback encoder because feature '{FeatureName}' is disabled.",
                nameof(features.EnableProcessedFrameEncoder));

            var encoding = ImageEncodingUtilities.Normalize(processedFrame.Encoding);
            using var encoded = processedFrame.ImmutableImage.Encode(ImageEncodingUtilities.ToSkiaFormat(encoding.Format), encoding.Quality);
            if (encoded is null)
            {
                _logger.LogWarning(
                    "Processed frame fallback encode failed; skipping export for frame #{FrameNumber} ({FrameId}).",
                    frameNumber,
                    processedFrame.FrameId);
                return;
            }

            var fallbackPayload = encoded.ToArray();
            var fallbackContentType = ImageEncodingUtilities.ToContentType(encoding.Format);
            var fallbackExtension = ImageEncodingUtilities.ToFileExtension(encoding.Format) ?? processedFrame.FileExtension;

            var fallbackMetadata = FrameExportMetadataBuilder.FromProcessed(
                processedFrame,
                stackResult.Context,
                rig,
                stageTimestampUtc,
                queueLatencyMilliseconds,
                processingMilliseconds,
                payloadContentType: fallbackContentType,
                payloadExtension: fallbackExtension);

            var fallbackEnvelope = new FrameExportEnvelope(
                processedFrame.FrameId,
                FrameExportStage.Processed,
                fallbackMetadata,
                fallbackPayload,
                fallbackContentType,
                fallbackExtension);

            if (!_dispatcher.TryEnqueue(fallbackEnvelope))
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
