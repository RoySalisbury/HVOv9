using System.Collections.Generic;

namespace HVO.SkyMonitorV5.Data.Configurations.Entities;

public sealed class CameraPipelineConfigEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public bool EnableStacking { get; set; }
    public bool EnableImageOverlays { get; set; }

    public int CaptureIntervalMilliseconds { get; set; }
    public int StackingFrameCount { get; set; }
    public int StackingBufferMinimumFrames { get; set; }
    public int StackingBufferIntegrationSeconds { get; set; }

    public int DayExposureMilliseconds { get; set; }
    public int NightExposureMilliseconds { get; set; }
    public int DayGain { get; set; }
    public int NightGain { get; set; }
    public int DayNightTransitionHourOffset { get; set; }

    public string OverlayTextFormat { get; set; } = string.Empty;

    public ImageEncodingSettings ProcessedImageEncoding { get; set; } = new();
    public BackgroundStackerSettings BackgroundStacker { get; set; } = new();
    public CapturePacingSettings CapturePacing { get; set; } = new();
    public RemoteDispatchSettings RemoteDispatch { get; set; } = new();
    public CardinalDirectionsOverlaySettings CardinalDirections { get; set; } = new();
    public CircularApertureMaskSettings CircularApertureMask { get; set; } = new();
    public ConstellationFigureOverlaySettings ConstellationFigures { get; set; } = new();
    public CelestialAnnotationSettings CelestialAnnotations { get; set; } = new();

    public IList<CameraPipelineFilterEntity> Filters { get; set; } = new List<CameraPipelineFilterEntity>();
}

public sealed class CameraPipelineFilterEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool Enabled { get; set; }
}

public sealed class ImageEncodingSettings
{
    public string Format { get; set; } = string.Empty;
    public int Quality { get; set; }
}

public sealed class BackgroundStackerSettings
{
    public bool Enabled { get; set; }
    public int QueueCapacity { get; set; }
    public string OverflowPolicy { get; set; } = string.Empty;
    public string CompressionMode { get; set; } = string.Empty;
    public int RestartDelaySeconds { get; set; }
    public AdaptiveBackgroundQueueSettings AdaptiveQueue { get; set; } = new();
}

public sealed class AdaptiveBackgroundQueueSettings
{
    public bool Enabled { get; set; }
    public int MinCapacity { get; set; }
    public int MaxCapacity { get; set; }
    public int IncreaseStep { get; set; }
    public int DecreaseStep { get; set; }
    public int ScaleUpThresholdPercent { get; set; }
    public int ScaleDownThresholdPercent { get; set; }
    public int EvaluationWindowSeconds { get; set; }
    public int CooldownSeconds { get; set; }
}

public sealed class CapturePacingSettings
{
    public bool Enabled { get; set; }
    public int ElevatedAdditionalDelayMilliseconds { get; set; }
    public int HighAdditionalDelayMilliseconds { get; set; }
    public int CriticalAdditionalDelayMilliseconds { get; set; }
    public int RejectionPenaltyMilliseconds { get; set; }
    public int RejectionPenaltyDurationSeconds { get; set; }
    public int RampUpStepMilliseconds { get; set; }
    public int RampDownStepMilliseconds { get; set; }
    public int MaxDelayMilliseconds { get; set; }
}

public sealed class RemoteDispatchSettings
{
    public bool Enabled { get; set; }
    public string Mode { get; set; } = string.Empty;
    public string? S3Bucket { get; set; }
    public string? FanoutExchange { get; set; }
    public string Region { get; set; } = string.Empty;
}

public sealed class CardinalDirectionsOverlaySettings
{
    public int OffsetXPixels { get; set; }
    public int OffsetYPixels { get; set; }
    public int RotationDegrees { get; set; }
    public int RadiusOffsetPixels { get; set; }
    public string LabelNorth { get; set; } = string.Empty;
    public string LabelSouth { get; set; } = string.Empty;
    public string LabelEast { get; set; } = string.Empty;
    public string LabelWest { get; set; } = string.Empty;
    public bool SwapEastWest { get; set; }
    public string CircleColor { get; set; } = string.Empty;
    public int CircleOpacity { get; set; }
    public int CircleThickness { get; set; }
    public string CircleLineStyle { get; set; } = string.Empty;
    public int LabelFillOpacity { get; set; }
    public int LabelPadding { get; set; }
    public int LabelCornerRadius { get; set; }
    public int LabelFontSize { get; set; }
}

public sealed class CircularApertureMaskSettings
{
    public int OffsetXPixels { get; set; }
    public int OffsetYPixels { get; set; }
    public int RadiusOffsetPixels { get; set; }
    public string MaskColor { get; set; } = string.Empty;
    public int MaskOpacity { get; set; }
}

public sealed class ConstellationFigureOverlaySettings
{
    public double LineThickness { get; set; }
    public double LineOpacity { get; set; }
    public string LineColor { get; set; } = string.Empty;
    public bool UseDashedLine { get; set; }
}

public sealed class CelestialAnnotationSettings
{
    public double LabelFontSize { get; set; }
    public string StarLabelColor { get; set; } = string.Empty;
    public string PlanetLabelColor { get; set; } = string.Empty;
    public string DeepSkyLabelColor { get; set; } = string.Empty;
    public double StarRingRadius { get; set; }
    public double PlanetRingRadius { get; set; }
    public double DeepSkyRingRadius { get; set; }
    public bool UseAutomaticStarSelection { get; set; }
    public int AutoStarCount { get; set; }
    public double AutoStarMagnitudeLimit { get; set; }
    public bool AnnotatePlanets { get; set; }
    public IList<CelestialAnnotationDeepSkyObjectEntity> DeepSkyObjects { get; set; } = new List<CelestialAnnotationDeepSkyObjectEntity>();
}

public sealed class CelestialAnnotationDeepSkyObjectEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public double RightAscensionHours { get; set; }
    public double DeclinationDegrees { get; set; }
    public double Magnitude { get; set; }
    public string Color { get; set; } = string.Empty;
}
