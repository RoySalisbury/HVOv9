using System;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Exports;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Skia;
using HVO.SkyMonitorV5.RPi.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Services;

public interface IFrameMediaProvider
{
    Task<FrameMedia?> GetProcessedFrameAsync(Guid frameId, DateTimeOffset timestamp, CancellationToken cancellationToken);

    Task<FrameMedia?> GetRawFrameAsync(Guid frameId, DateTimeOffset timestamp, RawFrameMediaFormat format, CancellationToken cancellationToken);
}

public enum RawFrameMediaFormat
{
    Png,
    Native
}

public sealed record FrameMedia(
    Guid FrameId,
    DateTimeOffset Timestamp,
    string ContentType,
    string FileExtension,
    string DataUri,
    byte[] Payload,
    FrameExportImageDescriptor? Descriptor = null)
{
    public string BuildDownloadFileName(string prefix)
        => FormattableString.Invariant($"{prefix}-{Timestamp:yyyyMMdd-HHmmss}.{FileExtension}");
}

internal sealed class FrameMediaProvider : IFrameMediaProvider
{
    private readonly ILocalApiClient _apiClient;
    private readonly IFrameStateStore _frameStateStore;
    private readonly IProcessedFrameEncoder _processedFrameEncoder;
    private readonly ILogger<FrameMediaProvider> _logger;
    private readonly IMemoryCache _cache;

    private static readonly MemoryCacheEntryOptions CacheOptions = new MemoryCacheEntryOptions()
        .SetSize(1)
        .SetSlidingExpiration(TimeSpan.FromMinutes(2));

    public FrameMediaProvider(
        ILocalApiClient apiClient,
        IFrameStateStore frameStateStore,
        IProcessedFrameEncoder processedFrameEncoder,
        IMemoryCache cache,
        ILogger<FrameMediaProvider> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _frameStateStore = frameStateStore ?? throw new ArgumentNullException(nameof(frameStateStore));
        _processedFrameEncoder = processedFrameEncoder ?? throw new ArgumentNullException(nameof(processedFrameEncoder));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<FrameMedia?> GetProcessedFrameAsync(Guid frameId, DateTimeOffset timestamp, CancellationToken cancellationToken)
    {
        var cacheKey = FrameCacheKey.Processed(frameId);
        return _cache.GetOrCreateAsync(cacheKey, entry => FetchProcessedFrameAsync(entry, frameId, timestamp, cancellationToken));
    }

    public Task<FrameMedia?> GetRawFrameAsync(Guid frameId, DateTimeOffset timestamp, RawFrameMediaFormat format, CancellationToken cancellationToken)
    {
        var cacheKey = FrameCacheKey.Raw(frameId, format);
        return _cache.GetOrCreateAsync(cacheKey, entry => FetchRawFrameAsync(entry, frameId, timestamp, format, cancellationToken));
    }

    private async Task<FrameMedia?> FetchProcessedFrameAsync(ICacheEntry cacheEntry, Guid frameId, DateTimeOffset timestamp, CancellationToken cancellationToken)
    {
        ConfigureCacheEntry(cacheEntry);

        LocalApiFrameResponse? apiResponse = null;
        try
        {
            apiResponse = await _apiClient.GetLatestProcessedFrameAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve processed frame from local API.");
        }

        if (apiResponse is not null && apiResponse.Payload.Length > 0)
        {
            if (apiResponse.FrameId is null || apiResponse.FrameId == frameId)
            {
                return BuildFrameMedia(frameId, apiResponse.Timestamp ?? timestamp, apiResponse.Payload, apiResponse.ContentType ?? "image/png", apiResponse.FileExtension ?? "png", descriptor: null);
            }

            _logger.LogDebug("Discarding processed frame from API due to mismatched frame id. Expected {ExpectedId}, received {ReceivedId}.", frameId, apiResponse.FrameId);
        }

        var processedFrame = _frameStateStore.LatestProcessedFrame;
        if (processedFrame is null || processedFrame.FrameId != frameId)
        {
            _logger.LogWarning("Processed frame {FrameId} is no longer available in the frame buffer.", frameId);
            return null;
        }

        var delivery = _processedFrameEncoder.Encode(processedFrame);
        var payload = delivery.Payload.ToArray();
        var contentType = delivery.ContentType;
        var extension = delivery.FileExtension ?? processedFrame.FileExtension ?? "png";

        return BuildFrameMedia(frameId, processedFrame.Timestamp, payload, contentType, extension, descriptor: null);
    }

    private async Task<FrameMedia?> FetchRawFrameAsync(ICacheEntry cacheEntry, Guid frameId, DateTimeOffset timestamp, RawFrameMediaFormat format, CancellationToken cancellationToken)
    {
        ConfigureCacheEntry(cacheEntry);

        var formatToken = format == RawFrameMediaFormat.Png ? "png" : "skimg";
        LocalApiFrameResponse? apiResponse = null;

        try
        {
            apiResponse = await _apiClient.GetLatestRawFrameAsync(formatToken, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve raw frame from local API for format {Format}.", formatToken);
        }

        if (apiResponse is not null && apiResponse.Payload.Length > 0)
        {
            if (apiResponse.FrameId is null || apiResponse.FrameId == frameId)
            {
                var contentType = apiResponse.ContentType ?? (format == RawFrameMediaFormat.Png ? "image/png" : SkiaRawFrameHelper.RawContentType);
                var extension = apiResponse.FileExtension ?? (format == RawFrameMediaFormat.Png ? "png" : "skimg");
                return BuildFrameMedia(frameId, apiResponse.Timestamp ?? timestamp, apiResponse.Payload, contentType, extension, apiResponse.Descriptor);
            }

            _logger.LogDebug("Discarding raw frame from API due to mismatched frame id. Expected {ExpectedId}, received {ReceivedId}.", frameId, apiResponse.FrameId);
        }

        var rawSnapshot = _frameStateStore.LatestRawFrame;
        if (rawSnapshot is null || rawSnapshot.FrameId != frameId)
        {
            _logger.LogWarning("Raw frame {FrameId} is no longer available in the frame buffer.", frameId);
            return null;
        }

        return format == RawFrameMediaFormat.Png
            ? BuildRawPngFromSnapshot(rawSnapshot)
            : BuildRawNativeFromSnapshot(rawSnapshot);
    }

    private FrameMedia? BuildRawPngFromSnapshot(RawFrameSnapshot snapshot)
    {
        using var encoded = snapshot.ImmutableImage?.Encode(SKEncodedImageFormat.Png, 92)
            ?? snapshot.Image.Encode(SKEncodedImageFormat.Png, 92);

        if (encoded is null || encoded.Size == 0)
        {
            _logger.LogWarning("Failed to encode PNG payload for raw frame {FrameId} during fallback.", snapshot.FrameId);
            return null;
        }

        var payload = encoded.ToArray();
        var descriptor = snapshot.ImageDescriptor ?? SkiaRawFrameHelper.TryCreateDescriptor(snapshot.Image);

        return BuildFrameMedia(snapshot.FrameId, snapshot.Timestamp, payload, "image/png", "png", descriptor);
    }

    private FrameMedia? BuildRawNativeFromSnapshot(RawFrameSnapshot snapshot)
    {
        var sourceImage = snapshot.ImmutableImage ?? SKImage.FromBitmap(snapshot.Image);

        try
        {
            if (sourceImage is null)
            {
                _logger.LogWarning("Raw frame {FrameId} did not expose an immutable image for native payload fallback.", snapshot.FrameId);
                return null;
            }

            if (!SkiaRawFrameHelper.TryCreateRawPayload(sourceImage, out var payload, out var descriptor))
            {
                _logger.LogWarning("Failed to extract native raw payload for frame {FrameId}.", snapshot.FrameId);
                return null;
            }

            return BuildFrameMedia(snapshot.FrameId, snapshot.Timestamp, payload, SkiaRawFrameHelper.RawContentType, SkiaRawFrameHelper.RawFileExtension, descriptor);
        }
        finally
        {
            if (!ReferenceEquals(sourceImage, snapshot.ImmutableImage))
            {
                sourceImage?.Dispose();
            }
        }
    }

    private static FrameMedia BuildFrameMedia(Guid frameId, DateTimeOffset timestamp, byte[] payload, string contentType, string fileExtension, FrameExportImageDescriptor? descriptor)
    {
        var dataUri = BuildDataUri(payload, contentType);
        return new FrameMedia(frameId, timestamp, contentType, fileExtension, dataUri, payload, descriptor);
    }

    private static void ConfigureCacheEntry(ICacheEntry entry)
    {
        entry.SetOptions(CacheOptions);
    }

    private static string BuildDataUri(byte[] payload, string contentType)
    {
        if (payload.Length == 0)
        {
            return string.Empty;
        }

        var mediaType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType;
        var base64 = Convert.ToBase64String(payload);
        return FormattableString.Invariant($"data:{mediaType};base64,{base64}");
    }

    private readonly record struct FrameCacheKey(Guid FrameId, string Variant)
    {
        public static FrameCacheKey Processed(Guid frameId) => new(frameId, "processed");

        public static FrameCacheKey Raw(Guid frameId, RawFrameMediaFormat format)
            => new(frameId, format == RawFrameMediaFormat.Png ? "raw:png" : "raw:native");
    }
}
