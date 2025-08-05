using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Profile information from NINA Advanced API
/// </summary>
public class ProfileInfo
{
    [JsonPropertyName("Name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("Description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("Id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("LastUsed")]
    public string LastUsed { get; set; } = string.Empty;

    [JsonPropertyName("ApplicationSettings")]
    public ApplicationSettings? ApplicationSettings { get; set; }

    [JsonPropertyName("CameraSettings")]
    public CameraSettings? CameraSettings { get; set; }

    [JsonPropertyName("AutoFocusSettings")]
    public AutoFocusSettings? AutoFocusSettings { get; set; }

    [JsonPropertyName("FramingAssistantSettings")]
    public FramingAssistantSettings? FramingAssistantSettings { get; set; }

    [JsonPropertyName("GuiderSettings")]
    public GuiderSettings? GuiderSettings { get; set; }

    [JsonPropertyName("PlateSolveSettings")]
    public PlateSolveSettings? PlateSolveSettings { get; set; }

    [JsonPropertyName("RotatorSettings")]
    public RotatorSettings? RotatorSettings { get; set; }

    [JsonPropertyName("FlatDeviceSettings")]
    public FlatDeviceSettings? FlatDeviceSettings { get; set; }

    [JsonPropertyName("ImageHistorySettings")]
    public ImageHistorySettings? ImageHistorySettings { get; set; }
}

public class ApplicationSettings
{
    [JsonPropertyName("Culture")]
    public string Culture { get; set; } = string.Empty;

    [JsonPropertyName("DevicePollingInterval")]
    public int DevicePollingInterval { get; set; }

    [JsonPropertyName("PageSize")]
    public int PageSize { get; set; }

    [JsonPropertyName("LogLevel")]
    public string LogLevel { get; set; } = string.Empty;
}

public class CameraSettings
{
    [JsonPropertyName("Id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("PixelSize")]
    public double PixelSize { get; set; }

    [JsonPropertyName("Gain")]
    public int Gain { get; set; }

    [JsonPropertyName("Offset")]
    public int Offset { get; set; }

    [JsonPropertyName("USBLimit")]
    public int USBLimit { get; set; }

    [JsonPropertyName("Temperature")]
    public double Temperature { get; set; }

    [JsonPropertyName("FilePath")]
    public string FilePath { get; set; } = string.Empty;

    [JsonPropertyName("FilePattern")]
    public string FilePattern { get; set; } = string.Empty;
}

public class AutoFocusSettings
{
    [JsonPropertyName("AutoFocusExposureTime")]
    public double AutoFocusExposureTime { get; set; }

    [JsonPropertyName("AutoFocusInitialOffsetStep")]
    public int AutoFocusInitialOffsetStep { get; set; }

    [JsonPropertyName("AutoFocusStepSize")]
    public int AutoFocusStepSize { get; set; }

    [JsonPropertyName("AutoFocusNumberOfFrames")]
    public int AutoFocusNumberOfFrames { get; set; }

    [JsonPropertyName("AutoFocusUseBrightestStars")]
    public int AutoFocusUseBrightestStars { get; set; }

    [JsonPropertyName("AutoFocusCurveFitting")]
    public string AutoFocusCurveFitting { get; set; } = string.Empty;

    [JsonPropertyName("AutoFocusMethod")]
    public string AutoFocusMethod { get; set; } = string.Empty;

    [JsonPropertyName("ContrastDetectionMethod")]
    public string ContrastDetectionMethod { get; set; } = string.Empty;

    [JsonPropertyName("BacklashCompensationModel")]
    public string BacklashCompensationModel { get; set; } = string.Empty;

    [JsonPropertyName("AutoFocusTimeoutSeconds")]
    public int AutoFocusTimeoutSeconds { get; set; }

    [JsonPropertyName("RSquaredThreshold")]
    public double RSquaredThreshold { get; set; }
}

public class FramingAssistantSettings
{
    [JsonPropertyName("CameraHeight")]
    public int CameraHeight { get; set; }

    [JsonPropertyName("CameraWidth")]
    public int CameraWidth { get; set; }

    [JsonPropertyName("FieldOfView")]
    public int FieldOfView { get; set; }

    [JsonPropertyName("Opacity")]
    public double Opacity { get; set; }

    [JsonPropertyName("LastSelectedImageSource")]
    public string LastSelectedImageSource { get; set; } = string.Empty;
}

public class GuiderSettings
{
    [JsonPropertyName("GuiderName")]
    public string GuiderName { get; set; } = string.Empty;

    [JsonPropertyName("PHD2Path")]
    public string PHD2Path { get; set; } = string.Empty;

    [JsonPropertyName("PHD2ProfileId")]
    public int PHD2ProfileId { get; set; }

    [JsonPropertyName("PHD2ServerUrl")]
    public string PHD2ServerUrl { get; set; } = string.Empty;

    [JsonPropertyName("PHD2ServerPort")]
    public int PHD2ServerPort { get; set; }

    [JsonPropertyName("MetaGuideUseIpAddressAny")]
    public bool MetaGuideUseIpAddressAny { get; set; }

    [JsonPropertyName("MetaGuidePort")]
    public int MetaGuidePort { get; set; }

    [JsonPropertyName("MGENFocalLength")]
    public int MGENFocalLength { get; set; }

    [JsonPropertyName("MGENPixelMargin")]
    public int MGENPixelMargin { get; set; }

    [JsonPropertyName("PreferredPlanetarium")]
    public string PreferredPlanetarium { get; set; } = string.Empty;
}

public class PlateSolveSettings
{
    [JsonPropertyName("AstrometryURL")]
    public string AstrometryURL { get; set; } = string.Empty;

    [JsonPropertyName("AstrometryAPIKey")]
    public string AstrometryAPIKey { get; set; } = string.Empty;

    [JsonPropertyName("BlindSolverType")]
    public string BlindSolverType { get; set; } = string.Empty;

    [JsonPropertyName("CygwinLocation")]
    public string CygwinLocation { get; set; } = string.Empty;

    [JsonPropertyName("ExposureTime")]
    public int ExposureTime { get; set; }

    [JsonPropertyName("Gain")]
    public int Gain { get; set; }

    [JsonPropertyName("Binning")]
    public int Binning { get; set; }

    [JsonPropertyName("PlateSolverType")]
    public string PlateSolverType { get; set; } = string.Empty;

    [JsonPropertyName("PS2Location")]
    public string PS2Location { get; set; } = string.Empty;

    [JsonPropertyName("PS3Location")]
    public string PS3Location { get; set; } = string.Empty;

    [JsonPropertyName("Regions")]
    public int Regions { get; set; }

    [JsonPropertyName("SearchRadius")]
    public int SearchRadius { get; set; }

    [JsonPropertyName("Threshold")]
    public int Threshold { get; set; }

    [JsonPropertyName("RotationTolerance")]
    public int RotationTolerance { get; set; }

    [JsonPropertyName("ReattemptDelay")]
    public int ReattemptDelay { get; set; }

    [JsonPropertyName("NumberOfAttempts")]
    public int NumberOfAttempts { get; set; }

    [JsonPropertyName("AspsLocation")]
    public string AspsLocation { get; set; } = string.Empty;

    [JsonPropertyName("ASTAPLocation")]
    public string ASTAPLocation { get; set; } = string.Empty;

    [JsonPropertyName("DownSampleFactor")]
    public int DownSampleFactor { get; set; }

    [JsonPropertyName("MaxObjects")]
    public int MaxObjects { get; set; }

    [JsonPropertyName("Sync")]
    public bool Sync { get; set; }

    [JsonPropertyName("SlewToTarget")]
    public bool SlewToTarget { get; set; }

    [JsonPropertyName("BlindFailoverEnabled")]
    public bool BlindFailoverEnabled { get; set; }

    [JsonPropertyName("TheSkyXHost")]
    public string TheSkyXHost { get; set; } = string.Empty;

    [JsonPropertyName("TheSkyXPort")]
    public int TheSkyXPort { get; set; }

    [JsonPropertyName("PinPointCatalogType")]
    public string PinPointCatalogType { get; set; } = string.Empty;

    [JsonPropertyName("PinPointCatalogRoot")]
    public string PinPointCatalogRoot { get; set; } = string.Empty;

    [JsonPropertyName("PinPointMaxMagnitude")]
    public int PinPointMaxMagnitude { get; set; }

    [JsonPropertyName("PinPointExpansion")]
    public int PinPointExpansion { get; set; }

    [JsonPropertyName("PinPointAllSkyApiKey")]
    public string PinPointAllSkyApiKey { get; set; } = string.Empty;

    [JsonPropertyName("PinPointAllSkyApiHost")]
    public string PinPointAllSkyApiHost { get; set; } = string.Empty;
}

public class RotatorSettings
{
    [JsonPropertyName("Id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("Reverse2")]
    public bool Reverse2 { get; set; }

    [JsonPropertyName("RangeType")]
    public string RangeType { get; set; } = string.Empty;

    [JsonPropertyName("RangeStartMechanicalPosition")]
    public int RangeStartMechanicalPosition { get; set; }
}

public class FlatDeviceSettings
{
    [JsonPropertyName("Id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("PortName")]
    public string PortName { get; set; } = string.Empty;

    [JsonPropertyName("UseSynchronizedOpen")]
    public bool UseSynchronizedOpen { get; set; }

    [JsonPropertyName("UseHttps")]
    public bool UseHttps { get; set; }
}

public class ImageHistorySettings
{
    [JsonPropertyName("ImageHistoryLeftSelected")]
    public string ImageHistoryLeftSelected { get; set; } = string.Empty;

    [JsonPropertyName("ImageHistoryRightSelected")]
    public string ImageHistoryRightSelected { get; set; } = string.Empty;
}
