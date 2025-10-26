using System.Text.Json.Serialization;
using HVO.SkyMonitorV5.RPi.Exports;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Models.System;
using Microsoft.AspNetCore.Mvc;

namespace HVO.SkyMonitorV5.RPi.Services;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AllSkyStatusResponse))]
[JsonSerializable(typeof(AllSkyStatusSummary))]
[JsonSerializable(typeof(AllSkyCameraSummary))]
[JsonSerializable(typeof(AllSkyRigSummary))]
[JsonSerializable(typeof(AllSkySensorSummary))]
[JsonSerializable(typeof(AllSkyLensSummary))]
[JsonSerializable(typeof(ExposureSettings))]
[JsonSerializable(typeof(ProcessedFrameSummary))]
[JsonSerializable(typeof(ExposureProfileSummary))]
[JsonSerializable(typeof(ExposureProfileBucketSummary))]
[JsonSerializable(typeof(ExposureAnalysisSummary))]
[JsonSerializable(typeof(ExposureOverrideSummary))]
[JsonSerializable(typeof(ExposureOverrideSnapshot))]
[JsonSerializable(typeof(ExposureOverrideBucket))]
[JsonSerializable(typeof(ExposureLightingCondition))]
[JsonSerializable(typeof(SystemObservatoryConfigurationResponse))]
[JsonSerializable(typeof(UpdateSystemObservatoryRequest))]
[JsonSerializable(typeof(SystemLocalApiConfigurationResponse))]
[JsonSerializable(typeof(UpdateSystemLocalApiRequest))]
[JsonSerializable(typeof(SystemTelemetryRetentionConfigurationResponse))]
[JsonSerializable(typeof(UpdateSystemTelemetryRetentionRequest))]
[JsonSerializable(typeof(TelemetryRetentionPolicyModel))]
[JsonSerializable(typeof(CameraDescriptor))]
[JsonSerializable(typeof(CameraConfiguration))]
[JsonSerializable(typeof(HVO.SkyMonitorV5.RPi.Cameras.Optics.CameraSpec))]
[JsonSerializable(typeof(HVO.SkyMonitorV5.RPi.Cameras.Optics.CameraCapabilities))]
[JsonSerializable(typeof(HVO.SkyMonitorV5.RPi.Cameras.Optics.SensorSpec))]
[JsonSerializable(typeof(HVO.SkyMonitorV5.RPi.Cameras.Optics.LensSpec))]
[JsonSerializable(typeof(FrameExportImageDescriptor))]
[JsonSerializable(typeof(RawFrameSummary))]
[JsonSerializable(typeof(BackgroundStackerStatus))]
[JsonSerializable(typeof(CapturePacingStatus))]
[JsonSerializable(typeof(ProcessingQueueStatus))]
[JsonSerializable(typeof(RemoteDispatchStatus))]
[JsonSerializable(typeof(HVO.SkyMonitorV5.RPi.Cameras.Projection.RigSpec))]
[JsonSerializable(typeof(RigRuntimeStatusResponse))]
[JsonSerializable(typeof(RigRuntimeActionResponse))]
[JsonSerializable(typeof(RigRuntimeActionRequest))]
[JsonSerializable(typeof(HVO.SkyMonitorV5.RPi.Pipeline.ImageEncodingSettings))]
[JsonSerializable(typeof(HVO.SkyMonitorV5.RPi.Pipeline.ImageEncodingFormat))]
internal partial class LocalApiJsonContext : JsonSerializerContext
{
}
