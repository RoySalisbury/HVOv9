using System;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.ImageHistory;
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
    private readonly IFitsFrameEncoder _fitsEncoder;
    private readonly ILogger<FrameExportPublisher> _logger;
    private readonly IOptionsMonitor<SkiaPipelineFeatureOptions> _featureOptions;
    private readonly ISkiaPipelineFeatureToggleMonitor _featureMonitor;
    private readonly IImageFrameArchiveIngestionQueue _archiveQueue;
    private readonly IOptionsMonitor<ImageHistoryOptions> _imageHistoryOptions;
    private readonly IOptionsMonitor<FrameExportOptions> _exportOptions;

    public FrameExportPublisher(
        IFrameExportDispatcher dispatcher,
        IProcessedFrameEncoder processedFrameEncoder,
        IFitsFrameEncoder fitsEncoder,
        ILogger<FrameExportPublisher> logger,
        IOptionsMonitor<SkiaPipelineFeatureOptions> featureOptions,
        ISkiaPipelineFeatureToggleMonitor featureMonitor,
        IImageFrameArchiveIngestionQueue archiveQueue,
        IOptionsMonitor<ImageHistoryOptions> imageHistoryOptions,
        IOptionsMonitor<FrameExportOptions> exportOptions)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _processedFrameEncoder = processedFrameEncoder ?? throw new ArgumentNullException(nameof(processedFrameEncoder));
        _fitsEncoder = fitsEncoder ?? throw new ArgumentNullException(nameof(fitsEncoder));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _featureOptions = featureOptions ?? throw new ArgumentNullException(nameof(featureOptions));
        _featureMonitor = featureMonitor ?? throw new ArgumentNullException(nameof(featureMonitor));
        _archiveQueue = archiveQueue ?? throw new ArgumentNullException(nameof(archiveQueue));
        _imageHistoryOptions = imageHistoryOptions ?? throw new ArgumentNullException(nameof(imageHistoryOptions));
        _exportOptions = exportOptions ?? throw new ArgumentNullException(nameof(exportOptions));
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

            // Check configured encoding format for raw frames
            var exportOptions = _exportOptions.CurrentValue;
            var rawOptions = exportOptions.Raw ?? new FrameExportStageOptions();
            var archiveEncoding = rawOptions.ArchiveEncoding;

            // If FITS is configured, use FITS encoder
            if (archiveEncoding.Format == ImageEncodingFormat.Fits)
            {
                try
                {
                    using var bitmap = SKBitmap.FromImage(imageForExport);
                    var fitsBytes = _fitsEncoder.EncodeRaw(bitmap, capture, rig, archiveEncoding.FitsOptions);

                    var metadata = FrameExportMetadataBuilder.FromRaw(
                        capture,
                        rig,
                        stageTimestampUtc,
                        queueLatencyMilliseconds: captureMilliseconds,
                        processingMilliseconds: null,
                        rawImageDescriptor: null,
                        payloadContentType: fitsBytes.ContentType,
                        payloadExtension: fitsBytes.FileExtension);

                    // Queue raw archive ingestion for FITS
                    QueueArchiveIngestion(metadata, fitsBytes.Payload.ToArray(), fitsBytes.ContentType, fitsBytes.FileExtension);

                    var envelope = new FrameExportEnvelope(
                        capture.FrameId,
                        FrameExportStage.Raw,
                        metadata,
                        fitsBytes.Payload,
                        fitsBytes.ContentType,
                        fitsBytes.FileExtension);

                    if (!_dispatcher.TryEnqueue(envelope))
                    {
                        _logger.LogDebug(
                            "Frame export dispatcher rejected raw FITS payload for frame #{FrameNumber} ({FrameId}).",
                            frameNumber,
                            capture.FrameId);
                    }
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "FITS encoding for raw frame #{FrameNumber} ({FrameId}) failed; falling back to Skia raw or PNG.", frameNumber, capture.FrameId);
                    // Fall through to legacy encoding
                }
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

                // Queue raw archive ingestion for native payload
                QueueArchiveIngestion(metadata, rawBytes, SkiaRawFrameHelper.RawContentType, SkiaRawFrameHelper.RawFileExtension);

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

            // Queue raw archive ingestion for PNG fallback
            QueueArchiveIngestion(fallbackMetadata, payload, LegacyRawContentType, LegacyRawExtension);

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
                // Route using unified export options: Delivery vs Archive can differ.
                var exportOptions = _exportOptions.CurrentValue;
                var processedOptions = exportOptions.Processed ?? new FrameExportStageOptions();

                var archiveEncoding = processedOptions.ArchiveEncoding;
                var deliveryEncoding = processedOptions.DeliveryEncoding ?? archiveEncoding;

                // Ensure delivery uses a raster-friendly format for UI/delivery
                if (!ImageEncodingUtilities.IsRasterFormat(deliveryEncoding.Format))
                {
                    deliveryEncoding = new ImageEncodingSettings(ImageEncodingFormat.Jpeg, 85);
                }

                // Encode delivery payload (UI-friendly raster)
                var delivery = _processedFrameEncoder.Encode(
                    processedFrame,
                    Services.ProcessedFrameEncodingContext.UserInterface,
                    deliveryEncoding);
                var deliveryContentType = delivery.ContentType;
                var deliveryExtension = delivery.FileExtension ?? processedFrame.FileExtension ?? TryGetFileExtension(deliveryContentType);

                var deliveryMetadata = FrameExportMetadataBuilder.FromProcessed(
                    processedFrame,
                    stackResult.Context,
                    rig,
                    stageTimestampUtc,
                    queueLatencyMilliseconds,
                    processingMilliseconds,
                    payloadContentType: deliveryContentType,
                    payloadExtension: deliveryExtension);

                var deliveryPayloadBuffer = delivery.Payload.ToArray();

                // Encode archive payload only when archive is enabled
                var imageHistory = _imageHistoryOptions.CurrentValue;
                if (imageHistory is not null && imageHistory.EnableArchive)
                {
                    var archive = _processedFrameEncoder.Encode(
                        processedFrame,
                        Services.ProcessedFrameEncodingContext.Export,
                        archiveEncoding);
                    var archiveContentType = archive.ContentType;
                    var archiveExtension = archive.FileExtension ?? processedFrame.FileExtension ?? TryGetFileExtension(archiveContentType);

                    var archiveMetadata = FrameExportMetadataBuilder.FromProcessed(
                        processedFrame,
                        stackResult.Context,
                        rig,
                        stageTimestampUtc,
                        queueLatencyMilliseconds,
                        processingMilliseconds,
                        payloadContentType: archiveContentType,
                        payloadExtension: archiveExtension);

                    var archivePayloadBuffer = archive.Payload.ToArray();
                    QueueArchiveIngestion(archiveMetadata, archivePayloadBuffer, archiveContentType, archiveExtension);
                }

                // Enqueue delivery envelope
                var payload = new ReadOnlyMemory<byte>(deliveryPayloadBuffer);
                var envelope = new FrameExportEnvelope(
                    processedFrame.FrameId,
                    FrameExportStage.Processed,
                    deliveryMetadata,
                    payload,
                    deliveryContentType,
                    deliveryExtension);

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

            QueueArchiveIngestion(fallbackMetadata, fallbackPayload, fallbackContentType, fallbackExtension);

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

    private void QueueArchiveIngestion(FrameExportMetadata metadata, byte[] payload, string contentType, string? fileExtension)
    {
        var options = _imageHistoryOptions.CurrentValue;
        if (options is null || !options.EnableArchive)
        {
            _logger.LogDebug("Archive ingestion skipped for frame {FrameId} - archive disabled.", metadata.FrameId);
            return;
        }

        var extension = fileExtension ?? metadata.PayloadExtension ?? TryGetFileExtension(contentType) ?? "jpg";
        var request = new ImageFrameArchiveIngestionRequest(
            metadata.FrameId,
            metadata,
            payload,
            contentType,
            extension,
            FrameExportPayloadRole.Archive);

        _logger.LogDebug(
            "Queuing archive ingestion for frame {FrameId} - ContentType: {ContentType}, Extension: {Extension}, PayloadRole: {PayloadRole}",
            metadata.FrameId,
            contentType,
            extension,
            FrameExportPayloadRole.Archive);

        if (!_archiveQueue.TryEnqueue(request))
        {
            _logger.LogWarning(
                "Image frame archive ingestion queue rejected processed frame {FrameId}.",
                metadata.FrameId);
        }
    }

}
