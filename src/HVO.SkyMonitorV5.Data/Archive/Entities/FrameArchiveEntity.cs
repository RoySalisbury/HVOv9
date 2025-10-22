using System;

namespace HVO.SkyMonitorV5.Data.Archive.Entities;

/// <summary>
/// Stores metadata and storage references for a processed (composed) frame that is available in the image history archive.
/// </summary>
public sealed class FrameArchiveEntity
{
    public Guid FrameId { get; set; }

    public DateTimeOffset CapturedAtUtc { get; set; }

    public string RigName { get; set; } = string.Empty;

    public string CameraName { get; set; } = string.Empty;

    public int FramesStacked { get; set; }

    public int? IntegrationMilliseconds { get; set; }

    public string[] AppliedFilters { get; set; } = Array.Empty<string>();

    public double? QueueLatencyMilliseconds { get; set; }

    public double? ProcessingMilliseconds { get; set; }

    public double? FullPipelineMilliseconds { get; set; }

    public string PayloadContentType { get; set; } = "image/jpeg";

    public string PayloadExtension { get; set; } = "jpg";

    public string? ThumbnailFilePath { get; set; }

    public string? ThumbnailObjectKey { get; set; }

    public string? ThumbnailBucket { get; set; }

    public string? MediaFilePath { get; set; }

    public string? MediaObjectKey { get; set; }

    public string? MediaBucket { get; set; }

    public string? RawMediaFilePath { get; set; }

    public string? RawMediaObjectKey { get; set; }

    public string? RawMediaBucket { get; set; }

    public DateTimeOffset ArchivedAtUtc { get; set; }
}
