using System;
using System.Collections.Generic;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Models;

namespace HVO.SkyMonitorV5.RPi.Exports;

internal static class FrameExportMetadataBuilder
{
    public static FrameExportMetadata FromRaw(
        CapturedImage capture,
        RigSpec rig,
        DateTimeOffset stageTimestampUtc,
        double? queueLatencyMilliseconds,
        double? processingMilliseconds)
    {
        var context = capture.Context;
        var rigInfo = ResolveRigInfo(context?.Rig ?? rig);
        var exposure = capture.Exposure ?? new ExposureSettings(0, 0, false, false);

        var normalizedQueueLatency = NormalizeDuration(queueLatencyMilliseconds);
        var normalizedProcessing = NormalizeDuration(processingMilliseconds);
        var integrationMilliseconds = exposure.ExposureMilliseconds;
        var fullPipelineMilliseconds = ComputeFullPipelineMilliseconds(
            exposure.ExposureMilliseconds,
            normalizedQueueLatency,
            normalizedProcessing);

        return new FrameExportMetadata(
            capture.FrameId,
            capture.Timestamp,
            stageTimestampUtc,
            exposure,
            rigInfo.RigName,
            rigInfo.CameraName,
            rigInfo.LensName,
            context?.LatitudeDeg ?? 0,
            context?.LongitudeDeg ?? 0,
            context?.FlipHorizontal ?? false,
            context?.HorizonPadding,
            context?.ApplyRefraction ?? false,
            FramesStacked: 1,
        IntegrationMilliseconds: integrationMilliseconds,
            AppliedFilters: null,
            QueueLatencyMilliseconds: normalizedQueueLatency,
            ProcessingMilliseconds: normalizedProcessing,
            FullPipelineMilliseconds: fullPipelineMilliseconds);
    }

    public static FrameExportMetadata FromProcessed(
        ProcessedFrame processed,
        FrameContext? context,
        RigSpec rig,
        DateTimeOffset stageTimestampUtc,
        double? queueLatencyMilliseconds,
        double? processingMilliseconds)
    {
        var rigInfo = ResolveRigInfo(context?.Rig ?? rig);
        var exposure = processed.Exposure ?? new ExposureSettings(0, 0, false, false);

        var normalizedQueueLatency = NormalizeDuration(queueLatencyMilliseconds);
        var effectiveProcessing = NormalizeDuration(processingMilliseconds ?? processed.ProcessingMilliseconds);
        var integrationMilliseconds = processed.IntegrationMilliseconds;
        var fullPipelineMilliseconds = ComputeFullPipelineMilliseconds(
            exposure.ExposureMilliseconds,
            normalizedQueueLatency,
            effectiveProcessing);

        IReadOnlyList<string>? filters = processed.AppliedFilters?.Count > 0
            ? processed.AppliedFilters
            : null;

        return new FrameExportMetadata(
            processed.FrameId,
            processed.Timestamp,
            stageTimestampUtc,
            exposure,
            rigInfo.RigName,
            rigInfo.CameraName,
            rigInfo.LensName,
            context?.LatitudeDeg ?? 0,
            context?.LongitudeDeg ?? 0,
            context?.FlipHorizontal ?? false,
            context?.HorizonPadding,
            context?.ApplyRefraction ?? false,
            processed.FramesStacked,
            processed.IntegrationMilliseconds,
            filters,
            QueueLatencyMilliseconds: normalizedQueueLatency,
            ProcessingMilliseconds: effectiveProcessing,
            FullPipelineMilliseconds: fullPipelineMilliseconds);
    }

    private static double? NormalizeDuration(double? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        var duration = value.Value;
        if (double.IsNaN(duration) || double.IsInfinity(duration))
        {
            return null;
        }

        if (duration < 0)
        {
            duration = 0d;
        }

        return duration;
    }

    private static double? ComputeFullPipelineMilliseconds(int? exposureMilliseconds, double? queueLatencyMilliseconds, double? processingMilliseconds)
    {
        var hasComponent = false;
        double total = 0d;

        if (exposureMilliseconds.HasValue)
        {
            total += Math.Max(0, exposureMilliseconds.Value);
            hasComponent = true;
        }

        if (queueLatencyMilliseconds.HasValue)
        {
            total += queueLatencyMilliseconds.Value;
            hasComponent = true;
        }

        if (processingMilliseconds.HasValue)
        {
            total += processingMilliseconds.Value;
            hasComponent = true;
        }

        return hasComponent ? total : null;
    }

    private static (string RigName, string CameraName, string LensName) ResolveRigInfo(RigSpec rig)
    {
        var rigName = rig?.Name ?? "Unknown";
        var descriptor = rig?.Camera?.Descriptor;
        var cameraName = descriptor?.AdapterName;
        if (string.IsNullOrWhiteSpace(cameraName))
        {
            cameraName = descriptor?.Model;
        }

        if (string.IsNullOrWhiteSpace(cameraName))
        {
            cameraName = descriptor?.Manufacturer;
        }

        if (string.IsNullOrWhiteSpace(cameraName))
        {
            cameraName = rigName;
        }

        var lensName = rig?.Lens?.Name;
        if (string.IsNullOrWhiteSpace(lensName))
        {
            lensName = "Unknown";
        }

        return (rigName, cameraName, lensName);
    }
}
