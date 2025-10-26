using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.Data.Abstractions;
using HVO.SkyMonitorV5.Data.Archive;
using HVO.SkyMonitorV5.Data.Archive.Entities;
using HVO.SkyMonitorV5.RPi.Exports;
using HVO.SkyMonitorV5.RPi.Exports.Sinks;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.ImageHistory;

/// <summary>
/// Background service that processes archive ingestion requests and writes records to the image frame archive store.
/// </summary>
internal sealed class ImageFrameArchiveIngestionService : BackgroundService, IImageFrameArchiveIngestionQueue
{
    private const int ChannelCapacity = 64;

    private readonly Channel<ImageFrameArchiveIngestionRequest> _channel;
    private readonly IDbContextFactory<ImageFrameArchiveContext> _archiveContextFactory;
    private readonly IOptionsMonitor<ImageHistoryOptions> _imageHistoryOptions;
    private readonly IOptionsMonitor<FrameExportOptions> _frameExportOptions;
    private readonly ISkyMonitorDataPathProvider _dataPathProvider;
    private readonly IObservatoryClock _clock;
    private readonly ILogger<ImageFrameArchiveIngestionService> _logger;

    public ImageFrameArchiveIngestionService(
        IDbContextFactory<ImageFrameArchiveContext> archiveContextFactory,
        IOptionsMonitor<ImageHistoryOptions> imageHistoryOptions,
        IOptionsMonitor<FrameExportOptions> frameExportOptions,
        ISkyMonitorDataPathProvider dataPathProvider,
        IObservatoryClock clock,
        ILogger<ImageFrameArchiveIngestionService> logger)
    {
        _archiveContextFactory = archiveContextFactory ?? throw new ArgumentNullException(nameof(archiveContextFactory));
        _imageHistoryOptions = imageHistoryOptions ?? throw new ArgumentNullException(nameof(imageHistoryOptions));
        _frameExportOptions = frameExportOptions ?? throw new ArgumentNullException(nameof(frameExportOptions));
        _dataPathProvider = dataPathProvider ?? throw new ArgumentNullException(nameof(dataPathProvider));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var boundedOptions = new BoundedChannelOptions(ChannelCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.DropOldest
        };

        _channel = Channel.CreateBounded<ImageFrameArchiveIngestionRequest>(boundedOptions);
    }

    public bool TryEnqueue(ImageFrameArchiveIngestionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!IsArchiveEnabled())
        {
            return false;
        }

        if (_channel.Writer.TryWrite(request))
        {
            return true;
        }

        _logger.LogDebug("Image frame archive ingestion channel is full; dropping frame {FrameId}.", request.FrameId);
        return false;
    }

    public async ValueTask<bool> EnqueueAsync(ImageFrameArchiveIngestionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!IsArchiveEnabled())
        {
            return false;
        }

        try
        {
            await _channel.Writer.WriteAsync(request, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (ChannelClosedException)
        {
            _logger.LogDebug("Image frame archive ingestion channel closed while enqueuing frame {FrameId}.", request.FrameId);
            return false;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Image frame archive ingestion service starting.");

        try
        {
            await foreach (var request in _channel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await ProcessRequestAsync(request, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to archive frame {FrameId}.", request.FrameId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Service is stopping.
        }

        _logger.LogInformation("Image frame archive ingestion service stopped.");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete();
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private bool IsArchiveEnabled()
    {
        var options = _imageHistoryOptions.CurrentValue;
        return options is not null && options.EnableArchive;
    }

    private async Task ProcessRequestAsync(ImageFrameArchiveIngestionRequest request, CancellationToken cancellationToken)
    {
        var options = _imageHistoryOptions.CurrentValue ?? new ImageHistoryOptions();
        if (!options.EnableArchive)
        {
            return;
        }

        var timestampUtc = FrameExportPathUtilities.ResolveStageTimestamp(request.Metadata);
        var isRaw = IsRawIngestion(request);

        var thumbnailPath = isRaw
            ? null
            : await TryPersistThumbnailAsync(request, options, timestampUtc, cancellationToken).ConfigureAwait(false);

        var processedRefs = ResolveMediaReferencesForStage(request, timestampUtc, FrameExportStage.Processed);
        var rawRefs = ResolveMediaReferencesForStage(request, timestampUtc, FrameExportStage.Raw);

        var entity = new FrameArchiveEntity
        {
            FrameId = request.FrameId,
            CapturedAtUtc = request.Metadata.CapturedAtUtc,
            RigName = request.Metadata.RigName,
            CameraName = request.Metadata.CameraName,
            FramesStacked = request.Metadata.FramesStacked ?? 0,
            IntegrationMilliseconds = request.Metadata.IntegrationMilliseconds,
            AppliedFilters = request.Metadata.AppliedFilters?.ToArray() ?? Array.Empty<string>(),
            QueueLatencyMilliseconds = request.Metadata.QueueLatencyMilliseconds,
            ProcessingMilliseconds = request.Metadata.ProcessingMilliseconds,
            FullPipelineMilliseconds = request.Metadata.FullPipelineMilliseconds,
            // For raw ingestions, avoid assigning null to non-nullable; keep a safe default.
            PayloadContentType = ResolveContentType(request),
            PayloadExtension = isRaw ? "jpg" : FrameExportPathUtilities.ResolveExtension(request.FileExtension ?? request.Metadata.PayloadExtension),
            ThumbnailFilePath = thumbnailPath,
            MediaFilePath = isRaw ? null : processedRefs.FilePath,
            MediaObjectKey = isRaw ? null : processedRefs.ObjectKey,
            MediaBucket = isRaw ? null : processedRefs.Bucket,
            RawMediaFilePath = isRaw ? rawRefs.FilePath : null,
            RawMediaObjectKey = isRaw ? rawRefs.ObjectKey : null,
            RawMediaBucket = isRaw ? rawRefs.Bucket : null,
            ArchivedAtUtc = _clock.UtcNow
        };

        await using var context = await _archiveContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Explicitly enable tracking for this query since the factory uses NoTracking by default
        // We need tracking so EF Core can detect changes when we update properties
        var existing = await context.FrameArchives
            .AsTracking()
            .FirstOrDefaultAsync(e => e.FrameId == entity.FrameId, cancellationToken)
            .ConfigureAwait(false);

        // Query is tracking-enabled above; avoid noisy state debug logs in production

        if (existing is null)
        {
            await context.FrameArchives.AddAsync(entity, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Update only fields relevant to the ingestion stage to avoid clobbering the other stage's references
            existing.CapturedAtUtc = entity.CapturedAtUtc;
            existing.RigName = entity.RigName;
            existing.CameraName = entity.CameraName;
            existing.FramesStacked = entity.FramesStacked;
            existing.IntegrationMilliseconds = entity.IntegrationMilliseconds;
            existing.AppliedFilters = entity.AppliedFilters;
            existing.QueueLatencyMilliseconds = entity.QueueLatencyMilliseconds;
            existing.ProcessingMilliseconds = entity.ProcessingMilliseconds;
            existing.FullPipelineMilliseconds = entity.FullPipelineMilliseconds;
            existing.ThumbnailFilePath = entity.ThumbnailFilePath ?? existing.ThumbnailFilePath;

            if (isRaw)
            {
                existing.RawMediaFilePath = entity.RawMediaFilePath ?? existing.RawMediaFilePath;
                existing.RawMediaObjectKey = entity.RawMediaObjectKey ?? existing.RawMediaObjectKey;
                existing.RawMediaBucket = entity.RawMediaBucket ?? existing.RawMediaBucket;
            }
            else
            {
                existing.PayloadContentType = entity.PayloadContentType ?? existing.PayloadContentType;
                existing.PayloadExtension = entity.PayloadExtension ?? existing.PayloadExtension;
                existing.MediaFilePath = entity.MediaFilePath ?? existing.MediaFilePath;
                existing.MediaObjectKey = entity.MediaObjectKey ?? existing.MediaObjectKey;
                existing.MediaBucket = entity.MediaBucket ?? existing.MediaBucket;
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private string ResolveContentType(ImageFrameArchiveIngestionRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ContentType))
        {
            return request.ContentType;
        }

        if (!string.IsNullOrWhiteSpace(request.Metadata.PayloadContentType))
        {
            return request.Metadata.PayloadContentType!;
        }

        return "image/jpeg";
    }

    private async Task<string?> TryPersistThumbnailAsync(ImageFrameArchiveIngestionRequest request, ImageHistoryOptions options, DateTimeOffset timestampUtc, CancellationToken cancellationToken)
    {
        try
        {
            var thumbnailBytes = CreateThumbnail(request.Payload, options);
            if (thumbnailBytes.Length == 0)
            {
                return null;
            }

            var baseDirectory = _dataPathProvider.ResolvePath(options.ThumbnailsRelativePath ?? ImageHistoryDefaults.DefaultThumbnailsRelativePath);
            var directory = Path.Combine(
                baseDirectory,
                timestampUtc.ToString("yyyy"),
                timestampUtc.ToString("MM"),
                timestampUtc.ToString("dd"));

            Directory.CreateDirectory(directory);

            var fileName = FormattableString.Invariant($"{FrameExportPathUtilities.BuildBaseFileName(timestampUtc, request.FrameId)}.jpg");
            var path = Path.Combine(directory, fileName);
            await File.WriteAllBytesAsync(path, thumbnailBytes, cancellationToken).ConfigureAwait(false);
            return path;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist thumbnail for frame {FrameId}.", request.FrameId);
            return null;
        }
    }

    private static byte[] CreateThumbnail(byte[] payload, ImageHistoryOptions options)
    {
        if (payload.Length == 0)
        {
            return Array.Empty<byte>();
        }

        using var data = SKData.CreateCopy(payload);
        using var image = SKImage.FromEncodedData(data);
        if (image is null)
        {
            return Array.Empty<byte>();
        }

        var maxAxis = Math.Max(image.Width, image.Height);
        var targetMaxAxis = Math.Clamp(options.ThumbnailMaxAxisPixels, 64, 2048);
        var targetQuality = Math.Clamp(options.ThumbnailQuality, 30, 100);

        using var bitmap = SKBitmap.FromImage(image);
        if (bitmap is null)
        {
            return Array.Empty<byte>();
        }

        SKBitmap? resizedBitmap = null;
        SKImage? encodeSource = null;
        try
        {
            if (maxAxis > targetMaxAxis)
            {
                var scale = targetMaxAxis / (float)maxAxis;
                var width = Math.Max(1, (int)Math.Round(image.Width * scale));
                var height = Math.Max(1, (int)Math.Round(image.Height * scale));
                var info = new SKImageInfo(width, height, bitmap.ColorType, bitmap.AlphaType, bitmap.ColorSpace);
                var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);
                resizedBitmap = bitmap.Resize(info, sampling);
            }

            var sourceBitmap = resizedBitmap ?? bitmap;
            encodeSource = SKImage.FromBitmap(sourceBitmap);
            using var encoded = encodeSource.Encode(SKEncodedImageFormat.Jpeg, targetQuality);
            return encoded?.ToArray() ?? Array.Empty<byte>();
        }
        finally
        {
            resizedBitmap?.Dispose();
            encodeSource?.Dispose();
        }
    }

    private (string? FilePath, string? ObjectKey, string? Bucket) ResolveMediaReferencesForStage(ImageFrameArchiveIngestionRequest request, DateTimeOffset timestampUtc, FrameExportStage stage)
    {
        var stageOptions = _frameExportOptions.CurrentValue.GetStageOptions(stage);
        if (!stageOptions.Enabled)
        {
            return (null, null, null);
        }

        var roles = stageOptions.EnumerateRoles().ToArray();
        if (!roles.Contains(request.PayloadRole))
        {
            return (null, null, null);
        }

        string? filePath = null;
        var filesystemOption = stageOptions.Filesystem.FirstOrDefault(static option => option is { Enabled: true, RootPathLength: > 0 });
        if (filesystemOption is not null)
        {
            filePath = FrameExportFilesystemPathHelper.BuildPayloadPath(
                filesystemOption,
                request.PayloadRole,
                timestampUtc,
                request.FrameId,
                request.FileExtension ?? request.Metadata.PayloadExtension);
        }

        string? objectKey = null;
        string? bucket = null;
        var s3Option = stageOptions.S3.FirstOrDefault(static option => option is { Enabled: true } && option.HasValidConfiguration);
        if (s3Option is not null)
        {
            objectKey = FormattableString.Invariant($"{s3Option.BuildObjectPrefix(request.PayloadRole, timestampUtc)}/{FrameExportPathUtilities.BuildBaseFileName(timestampUtc, request.FrameId)}.{FrameExportPathUtilities.ResolveExtension(request.FileExtension ?? request.Metadata.PayloadExtension)}");
            bucket = s3Option.Bucket;
        }

        return (filePath, objectKey, bucket);
    }

    private static bool IsRawIngestion(ImageFrameArchiveIngestionRequest request)
    {
        // Prefer explicit descriptor
        if (request.Metadata.RawImageDescriptor is not null)
        {
            return true;
        }

        var ct = (request.ContentType ?? request.Metadata.PayloadContentType)?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(ct))
        {
            return false;
        }

        return ct.Contains("application/fits") || ct.Contains(Skia.SkiaRawFrameHelper.RawContentType);
    }

    private static class ImageHistoryDefaults
    {
        public const string DefaultThumbnailsRelativePath = "telemetry/image-history/thumbnails";
    }
}
