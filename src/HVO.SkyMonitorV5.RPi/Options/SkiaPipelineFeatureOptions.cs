using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.RPi.Options;

/// <summary>
/// Configurable feature toggles for the Skia-based frame processing pipeline.
/// </summary>
public sealed class SkiaPipelineFeatureOptions
{
    public const string SectionName = "SkiaPipelineFeatures";

    /// <summary>
    /// When enabled, raw frame exports use the linear high-bit payload produced by <see cref="SkiaRawFrameHelper"/>.
    /// When disabled, raw exports fall back to PNG encoding for staged rollout or emergency rollback.
    /// </summary>
    [Display(Name = "Enable Raw Linear Payloads")]
    public bool EnableRawLinearPayloads { get; set; } = true;

    /// <summary>
    /// When enabled, processed frame exports use <see cref="IProcessedFrameEncoder"/> for delivery payloads.
    /// When disabled, the system encodes processed frames directly from their immutable image.
    /// </summary>
    [Display(Name = "Enable Processed Frame Encoder")]
    public bool EnableProcessedFrameEncoder { get; set; } = true;
}
