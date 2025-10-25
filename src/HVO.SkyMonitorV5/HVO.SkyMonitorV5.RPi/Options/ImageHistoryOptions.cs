using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.RPi.Options;

/// <summary>
/// Configuration that controls the Image History archive ingestion pipeline and thumbnail generation.
/// </summary>
public sealed class ImageHistoryOptions
{
    public const string SectionName = "ImageHistory";

    /// <summary>
    /// When true, the processed frame archive ingestion pipeline is enabled and frames are recorded to the archive store.
    /// </summary>
    public bool EnableArchive { get; set; }

    /// <summary>
    /// Number of days to retain archived frames. Future workers will interpret this value when pruning the archive.
    /// </summary>
    [Range(1, 3650)]
    public int RetentionDays { get; set; } = 90;

    /// <summary>
    /// Relative path (beneath the configured data root) where generated thumbnails are stored.
    /// </summary>
    [MaxLength(512)]
    public string ThumbnailsRelativePath { get; set; } = "telemetry/image-history/thumbnails";

    /// <summary>
    /// Maximum width or height, in pixels, for generated thumbnail images.
    /// </summary>
    [Range(64, 2048)]
    public int ThumbnailMaxAxisPixels { get; set; } = 320;

    /// <summary>
    /// JPEG quality value (0-100) used when encoding thumbnails.
    /// </summary>
    [Range(30, 100)]
    public int ThumbnailQuality { get; set; } = 86;
}
