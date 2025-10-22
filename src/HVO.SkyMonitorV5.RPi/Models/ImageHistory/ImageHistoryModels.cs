using System;
using System.Collections.Generic;

namespace HVO.SkyMonitorV5.RPi.Models.ImageHistory;

public sealed record ImageHistoryThumbnailEntry(
    Guid FrameId,
    DateTimeOffset CapturedAtUtc,
    DateTimeOffset CapturedAtLocal,
    string RigName,
    string CameraName,
    int FramesStacked,
    int? IntegrationMilliseconds,
    IReadOnlyList<string> AppliedFilters,
    double? QueueLatencyMilliseconds,
    double? ProcessingMilliseconds,
    double? FullPipelineMilliseconds,
    string PayloadContentType,
    string PayloadExtension,
    bool ThumbnailAvailable,
    bool MediaAvailable,
    bool RawMediaAvailable,
    DateTimeOffset ArchivedAtUtc,
    DateTimeOffset ArchivedAtLocal);

public sealed record ImageHistoryThumbnailPage(
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset GeneratedAtLocal,
    string TimeZoneDisplayName,
    int PageSize,
    IReadOnlyList<ImageHistoryThumbnailEntry> Items,
    string? NextCursor);

public sealed record ImageHistoryFrameDetail(
    Guid FrameId,
    DateTimeOffset CapturedAtUtc,
    DateTimeOffset CapturedAtLocal,
    string RigName,
    string CameraName,
    int FramesStacked,
    int? IntegrationMilliseconds,
    IReadOnlyList<string> AppliedFilters,
    double? QueueLatencyMilliseconds,
    double? ProcessingMilliseconds,
    double? FullPipelineMilliseconds,
    string PayloadContentType,
    string PayloadExtension,
    bool ThumbnailAvailable,
    bool MediaAvailable,
    bool RawMediaAvailable,
    DateTimeOffset ArchivedAtUtc,
    DateTimeOffset ArchivedAtLocal);

public sealed record ImageHistoryMediaReferences(
    string? ThumbnailFilePath,
    string? ThumbnailObjectKey,
    string? ThumbnailBucket,
    string? MediaFilePath,
    string? MediaObjectKey,
    string? MediaBucket,
    string? RawMediaFilePath,
    string? RawMediaObjectKey,
    string? RawMediaBucket);

public sealed record ImageHistoryFrameDetailResult(
    ImageHistoryFrameDetail Detail,
    ImageHistoryMediaReferences Media);

public sealed record ImageHistoryStatsResponse(
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset GeneratedAtLocal,
    string TimeZoneDisplayName,
    long FrameCount,
    DateTimeOffset? OldestCapturedAtUtc,
    DateTimeOffset? OldestCapturedAtLocal,
    DateTimeOffset? NewestCapturedAtUtc,
    DateTimeOffset? NewestCapturedAtLocal,
    double? AverageQueueLatencyMilliseconds,
    double? AverageProcessingMilliseconds,
    double? AverageFullPipelineMilliseconds,
    IReadOnlyList<ImageHistoryBreakdownEntry> RigBreakdown,
    IReadOnlyList<ImageHistoryBreakdownEntry> CameraBreakdown);

public sealed record ImageHistoryBreakdownEntry(string Name, long Count);

public sealed record ImageHistoryThumbnailsRequest(
    DateTimeOffset? Since,
    DateTimeOffset? Until,
    string? RigName,
    string? CameraName,
    int PageSize,
    string? Cursor);

public sealed record ImageHistoryStatsRequest(
    DateTimeOffset? Since,
    DateTimeOffset? Until,
    string? RigName,
    string? CameraName);

public sealed record ImageHistoryFilterState(
    TimeSpan Lookback,
    string? RigName,
    string? CameraName,
    int PageSize)
{
    public static ImageHistoryFilterState CreateDefault()
        => new(TimeSpan.FromHours(12), null, null, 60);
}

public sealed record ImageHistoryThumbnailViewModel(
    ImageHistoryThumbnailEntry Entry,
    DateTimeOffset CapturedAtLocal,
    string CaptureLabel,
    string GroupKey,
    string GroupLabel,
    string Subtitle,
    string MetadataSummary,
    string ThumbnailUri);

public sealed record ImageHistoryFrameDetailViewModel(
    ImageHistoryFrameDetail Detail,
    string CaptureLocalDisplay,
    string CaptureUtcDisplay,
    string ArchivedLocalDisplay,
    string ArchivedUtcDisplay,
    string RigSummary,
    string CameraSummary,
    string ProcessingSummary,
    string EncodingSummary,
    IReadOnlyList<string> AppliedFilters,
    string ProcessedMediaUri,
    string ProcessedDetailUri,
    string ProcessedDownloadUri,
    string? RawDownloadUri,
    string? ThumbnailDownloadUri,
    IReadOnlyList<string> MetadataHints);

public sealed record ImageHistoryChartSeriesViewModel(
    string Title,
    IReadOnlyList<double> Values,
    string Color,
    string YAxisLabel,
    string ValueSuffix);
