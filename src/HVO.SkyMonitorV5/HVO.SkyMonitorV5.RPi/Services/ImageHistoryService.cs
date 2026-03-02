using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HVO.Core.Results;
using HVO.SkyMonitorV5.Data.Archive;
using HVO.SkyMonitorV5.Data.Archive.Entities;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Models.ImageHistory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Services;

internal sealed class ImageHistoryService : IImageHistoryService
{
    private const int DefaultPageSize = 100;
    private const int MaxPageSize = 250;

    private readonly IDbContextFactory<ImageFrameArchiveContext> _contextFactory;
    private readonly IObservatoryClock _clock;
    private readonly ILogger<ImageHistoryService> _logger;

    public ImageHistoryService(
        IDbContextFactory<ImageFrameArchiveContext> contextFactory,
        IObservatoryClock clock,
        ILogger<ImageHistoryService> logger)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<ImageHistoryThumbnailPage>> GetThumbnailsAsync(ImageHistoryThumbnailsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pageSize = NormalizePageSize(request.PageSize);

            CursorToken? cursorToken = null;
            if (!string.IsNullOrWhiteSpace(request.Cursor))
            {
                if (!TryDecodeCursor(request.Cursor!, out var decoded))
                {
                    return Result<ImageHistoryThumbnailPage>.Failure(new ArgumentException("The supplied cursor token is invalid.", nameof(request.Cursor)));
                }

                cursorToken = decoded;
            }

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var filteredQuery = ApplyFilters(
                context.FrameArchives.AsNoTracking(),
                request.Since,
                request.Until,
                request.RigName,
                request.CameraName);

            IQueryable<FrameArchiveEntity> orderedQuery = filteredQuery
                .OrderByDescending(frame => frame.CapturedAtUtc)
                .ThenByDescending(frame => frame.FrameId);

            if (cursorToken is CursorToken token)
            {
                orderedQuery = orderedQuery.Where(frame =>
                    frame.CapturedAtUtc < token.CapturedAtUtc
                    || (frame.CapturedAtUtc == token.CapturedAtUtc && frame.FrameId.CompareTo(token.FrameId) < 0));
            }

            var entities = await orderedQuery
                .Take(pageSize + 1)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var hasMore = entities.Count > pageSize;
            string? nextCursor = null;

            if (hasMore)
            {
                var cursorEntity = entities[pageSize - 1];
                nextCursor = EncodeCursor(cursorEntity.CapturedAtUtc, cursorEntity.FrameId);
                entities.RemoveRange(pageSize, entities.Count - pageSize);
            }

            var items = entities
                .Select(MapThumbnail)
                .ToList();

            var page = new ImageHistoryThumbnailPage(
                GeneratedAtUtc: _clock.UtcNow,
                GeneratedAtLocal: _clock.LocalNow,
                TimeZoneDisplayName: _clock.TimeZoneDisplayName,
                PageSize: pageSize,
                Items: items,
                NextCursor: nextCursor);

            return Result<ImageHistoryThumbnailPage>.Success(page);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            _logger.TryLogOperationCanceled(ex, cancellationToken, "Image history thumbnail query cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve image history thumbnail page.");
            return Result<ImageHistoryThumbnailPage>.Failure(ex);
        }
    }

    public async Task<Result<ImageHistoryFrameDetailResult>> GetFrameAsync(Guid frameId, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var entity = await context.FrameArchives
                .AsNoTracking()
                .FirstOrDefaultAsync(frame => frame.FrameId == frameId, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                _logger.LogDebug("Requested image history frame {FrameId} does not exist in the archive.", frameId);
                return Result<ImageHistoryFrameDetailResult>.Failure(new InvalidOperationException(FormattableString.Invariant($"Frame {frameId} was not found in the image history archive.")));
            }

            var result = MapDetail(entity);
            return Result<ImageHistoryFrameDetailResult>.Success(result);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            _logger.TryLogOperationCanceled(ex, cancellationToken, "Image history frame query cancelled for FrameId {FrameId}.", frameId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve image history frame detail for FrameId {FrameId}.", frameId);
            return Result<ImageHistoryFrameDetailResult>.Failure(ex);
        }
    }

    public async Task<Result<ImageHistoryStatsResponse>> GetStatsAsync(ImageHistoryStatsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (request.Since.HasValue && request.Until.HasValue && request.Until < request.Since)
            {
                return Result<ImageHistoryStatsResponse>.Failure(new ArgumentException("Until must be greater than or equal to Since.", nameof(request.Until)));
            }

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var filteredQuery = ApplyFilters(
                context.FrameArchives.AsNoTracking(),
                request.Since,
                request.Until,
                request.RigName,
                request.CameraName);

            var totalCount = await filteredQuery.LongCountAsync(cancellationToken).ConfigureAwait(false);
            if (totalCount == 0)
            {
                var empty = new ImageHistoryStatsResponse(
                    GeneratedAtUtc: _clock.UtcNow,
                    GeneratedAtLocal: _clock.LocalNow,
                    TimeZoneDisplayName: _clock.TimeZoneDisplayName,
                    FrameCount: 0,
                    OldestCapturedAtUtc: null,
                    OldestCapturedAtLocal: null,
                    NewestCapturedAtUtc: null,
                    NewestCapturedAtLocal: null,
                    AverageQueueLatencyMilliseconds: null,
                    AverageProcessingMilliseconds: null,
                    AverageFullPipelineMilliseconds: null,
                    RigBreakdown: Array.Empty<ImageHistoryBreakdownEntry>(),
                    CameraBreakdown: Array.Empty<ImageHistoryBreakdownEntry>());

                return Result<ImageHistoryStatsResponse>.Success(empty);
            }

            var newestUtc = await filteredQuery
                .OrderByDescending(frame => frame.CapturedAtUtc)
                .Select(frame => frame.CapturedAtUtc)
                .FirstAsync(cancellationToken)
                .ConfigureAwait(false);

            var oldestUtc = await filteredQuery
                .OrderBy(frame => frame.CapturedAtUtc)
                .Select(frame => frame.CapturedAtUtc)
                .FirstAsync(cancellationToken)
                .ConfigureAwait(false);

            var averageQueue = await filteredQuery.AverageAsync(frame => frame.QueueLatencyMilliseconds, cancellationToken).ConfigureAwait(false);
            var averageProcessing = await filteredQuery.AverageAsync(frame => frame.ProcessingMilliseconds, cancellationToken).ConfigureAwait(false);
            var averageFullPipeline = await filteredQuery.AverageAsync(frame => frame.FullPipelineMilliseconds, cancellationToken).ConfigureAwait(false);

            var rigBreakdown = await filteredQuery
                .GroupBy(frame => frame.RigName)
                .Select(group => new ImageHistoryBreakdownEntry(group.Key, group.LongCount()))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var cameraBreakdown = await filteredQuery
                .GroupBy(frame => frame.CameraName)
                .Select(group => new ImageHistoryBreakdownEntry(group.Key, group.LongCount()))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var response = new ImageHistoryStatsResponse(
                GeneratedAtUtc: _clock.UtcNow,
                GeneratedAtLocal: _clock.LocalNow,
                TimeZoneDisplayName: _clock.TimeZoneDisplayName,
                FrameCount: totalCount,
                OldestCapturedAtUtc: oldestUtc,
                OldestCapturedAtLocal: _clock.ToLocal(oldestUtc),
                NewestCapturedAtUtc: newestUtc,
                NewestCapturedAtLocal: _clock.ToLocal(newestUtc),
                AverageQueueLatencyMilliseconds: averageQueue,
                AverageProcessingMilliseconds: averageProcessing,
                AverageFullPipelineMilliseconds: averageFullPipeline,
                RigBreakdown: rigBreakdown,
                CameraBreakdown: cameraBreakdown);

            return Result<ImageHistoryStatsResponse>.Success(response);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            _logger.TryLogOperationCanceled(ex, cancellationToken, "Image history stats query cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compute image history statistics.");
            return Result<ImageHistoryStatsResponse>.Failure(ex);
        }
    }

    private static IQueryable<FrameArchiveEntity> ApplyFilters(
        IQueryable<FrameArchiveEntity> source,
        DateTimeOffset? since,
        DateTimeOffset? until,
        string? rigName,
        string? cameraName)
    {
        if (since.HasValue)
        {
            var sinceUtc = since.Value;
            source = source.Where(frame => frame.CapturedAtUtc >= sinceUtc);
        }

        if (until.HasValue)
        {
            var untilUtc = until.Value;
            source = source.Where(frame => frame.CapturedAtUtc <= untilUtc);
        }

        if (!string.IsNullOrWhiteSpace(rigName))
        {
            source = source.Where(frame => frame.RigName == rigName);
        }

        if (!string.IsNullOrWhiteSpace(cameraName))
        {
            source = source.Where(frame => frame.CameraName == cameraName);
        }

        return source;
    }

    private ImageHistoryThumbnailEntry MapThumbnail(FrameArchiveEntity entity)
    {
        var detail = MapFrameDetail(entity);
        return new ImageHistoryThumbnailEntry(
            detail.FrameId,
            detail.CapturedAtUtc,
            detail.CapturedAtLocal,
            detail.RigName,
            detail.CameraName,
            detail.FramesStacked,
            detail.IntegrationMilliseconds,
            detail.AppliedFilters,
            detail.QueueLatencyMilliseconds,
            detail.ProcessingMilliseconds,
            detail.FullPipelineMilliseconds,
            detail.PayloadContentType,
            detail.PayloadExtension,
            detail.ThumbnailAvailable,
            detail.MediaAvailable,
            detail.RawMediaAvailable,
            detail.ArchivedAtUtc,
            detail.ArchivedAtLocal);
    }

    private ImageHistoryFrameDetailResult MapDetail(FrameArchiveEntity entity)
    {
        var detail = MapFrameDetail(entity);
        var media = MapMediaReferences(entity);
        return new ImageHistoryFrameDetailResult(detail, media);
    }

    private ImageHistoryFrameDetail MapFrameDetail(FrameArchiveEntity entity)
    {
        var capturedAtUtc = ResolveCapturedAtUtc(entity);
        var archivedAtUtc = ResolveArchivedAtUtc(entity);

        IReadOnlyList<string> appliedFilters = entity.AppliedFilters is { Length: > 0 }
            ? Array.AsReadOnly(entity.AppliedFilters.ToArray())
            : Array.Empty<string>();

        return new ImageHistoryFrameDetail(
            FrameId: entity.FrameId,
            CapturedAtUtc: capturedAtUtc,
            CapturedAtLocal: _clock.ToLocal(capturedAtUtc),
            RigName: entity.RigName,
            CameraName: entity.CameraName,
            FramesStacked: entity.FramesStacked,
            IntegrationMilliseconds: entity.IntegrationMilliseconds,
            AppliedFilters: appliedFilters,
            QueueLatencyMilliseconds: entity.QueueLatencyMilliseconds,
            ProcessingMilliseconds: entity.ProcessingMilliseconds,
            FullPipelineMilliseconds: entity.FullPipelineMilliseconds,
            PayloadContentType: ResolveContentType(entity),
            PayloadExtension: ResolveExtension(entity),
            ThumbnailAvailable: HasThumbnail(entity),
            MediaAvailable: HasMedia(entity),
            RawMediaAvailable: HasRawMedia(entity),
            ArchivedAtUtc: archivedAtUtc,
            ArchivedAtLocal: _clock.ToLocal(archivedAtUtc));
    }

    private static ImageHistoryMediaReferences MapMediaReferences(FrameArchiveEntity entity)
    {
        return new ImageHistoryMediaReferences(
            ThumbnailFilePath: entity.ThumbnailFilePath,
            ThumbnailObjectKey: entity.ThumbnailObjectKey,
            ThumbnailBucket: entity.ThumbnailBucket,
            MediaFilePath: entity.MediaFilePath,
            MediaObjectKey: entity.MediaObjectKey,
            MediaBucket: entity.MediaBucket,
            RawMediaFilePath: entity.RawMediaFilePath,
            RawMediaObjectKey: entity.RawMediaObjectKey,
            RawMediaBucket: entity.RawMediaBucket);
    }

    private DateTimeOffset ResolveCapturedAtUtc(FrameArchiveEntity entity)
    {
        if (entity.CapturedAtUtc != default)
        {
            return entity.CapturedAtUtc;
        }

        if (entity.ArchivedAtUtc != default)
        {
            return entity.ArchivedAtUtc;
        }

        return _clock.UtcNow;
    }

    private DateTimeOffset ResolveArchivedAtUtc(FrameArchiveEntity entity)
    {
        if (entity.ArchivedAtUtc != default)
        {
            return entity.ArchivedAtUtc;
        }

        if (entity.CapturedAtUtc != default)
        {
            return entity.CapturedAtUtc;
        }

        return _clock.UtcNow;
    }

    private static bool HasThumbnail(FrameArchiveEntity entity)
        => !string.IsNullOrWhiteSpace(entity.ThumbnailFilePath) || !string.IsNullOrWhiteSpace(entity.ThumbnailObjectKey);

    private static bool HasMedia(FrameArchiveEntity entity)
        => !string.IsNullOrWhiteSpace(entity.MediaFilePath) || !string.IsNullOrWhiteSpace(entity.MediaObjectKey);

    private static bool HasRawMedia(FrameArchiveEntity entity)
        => !string.IsNullOrWhiteSpace(entity.RawMediaFilePath) || !string.IsNullOrWhiteSpace(entity.RawMediaObjectKey);

    private static string ResolveContentType(FrameArchiveEntity entity)
        => !string.IsNullOrWhiteSpace(entity.PayloadContentType) ? entity.PayloadContentType : "image/jpeg";

    private static string ResolveExtension(FrameArchiveEntity entity)
        => !string.IsNullOrWhiteSpace(entity.PayloadExtension) ? entity.PayloadExtension : "jpg";

    private static int NormalizePageSize(int pageSize)
    {
        if (pageSize <= 0)
        {
            return DefaultPageSize;
        }

        return Math.Clamp(pageSize, 1, MaxPageSize);
    }

    private static string EncodeCursor(DateTimeOffset capturedAtUtc, Guid frameId)
    {
        var payload = FormattableString.Invariant($"{capturedAtUtc.UtcTicks}|{frameId:D}");
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
    }

    private static bool TryDecodeCursor(string cursor, out CursorToken token)
    {
        token = default;

        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                return false;
            }

            if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks))
            {
                return false;
            }

            if (!Guid.TryParse(parts[1], out var frameId))
            {
                return false;
            }

            var capturedAtUtc = new DateTimeOffset(ticks, TimeSpan.Zero);
            token = new CursorToken(capturedAtUtc, frameId);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private readonly record struct CursorToken(DateTimeOffset CapturedAtUtc, Guid FrameId);
}
