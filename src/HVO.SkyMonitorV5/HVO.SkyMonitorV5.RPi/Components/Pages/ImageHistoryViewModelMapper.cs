using System;
using System.Collections.Generic;
using System.Globalization;
using HVO.SkyMonitorV5.RPi.Models.ImageHistory;
using Microsoft.AspNetCore.Components;

namespace HVO.SkyMonitorV5.RPi.Components.Pages;

internal static class ImageHistoryViewModelMapper
{
    public static ImageHistoryFrameDetailViewModel CreateDetailViewModel(
        ImageHistoryFrameDetailResult result,
        NavigationManager navigationManager)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(navigationManager);

        var detail = result.Detail;
        var captureLocal = detail.CapturedAtLocal;
        var captureUtc = detail.CapturedAtUtc;
        var archivedLocal = detail.ArchivedAtLocal;
        var archivedUtc = detail.ArchivedAtUtc;

        var processedUri = BuildMediaUri(navigationManager, detail.FrameId, "processed", detail.ArchivedAtUtc);
        var processedDownload = processedUri;
        var rawDownload = detail.RawMediaAvailable
            ? BuildMediaUri(navigationManager, detail.FrameId, "raw", detail.ArchivedAtUtc, "png")
            : null;
        var thumbnailDownload = detail.ThumbnailAvailable
            ? BuildMediaUri(navigationManager, detail.FrameId, "thumbnail", detail.ArchivedAtUtc)
            : null;
        var processedDetailUri = FormattableString.Invariant($"/image-history/frame/{detail.FrameId:D}");

        var rigSummary = string.Format(CultureInfo.CurrentCulture, "Rig · {0}", detail.RigName);
        var cameraSummary = string.Format(CultureInfo.CurrentCulture, "Camera · {0}", detail.CameraName);
        var encodingSummary = string.Format(CultureInfo.CurrentCulture, "{0} ({1})", detail.PayloadContentType, detail.PayloadExtension);

        var processingParts = new List<string>();
        if (detail.QueueLatencyMilliseconds is { } queue)
        {
            processingParts.Add(string.Format(CultureInfo.CurrentCulture, "Queue {0:F0} ms", queue));
        }

        if (detail.ProcessingMilliseconds is { } processing)
        {
            processingParts.Add(string.Format(CultureInfo.CurrentCulture, "Process {0:F0} ms", processing));
        }

        if (detail.FullPipelineMilliseconds is { } full)
        {
            processingParts.Add(string.Format(CultureInfo.CurrentCulture, "Pipeline {0:F0} ms", full));
        }

        var processingSummary = processingParts.Count > 0
            ? string.Join(" · ", processingParts)
            : "Latency unavailable";

        var metadataHints = new List<string>
        {
            string.Format(CultureInfo.CurrentCulture, "Frame ID · {0}", detail.FrameId)
        };

        if (detail.FramesStacked > 0)
        {
            metadataHints.Add(string.Format(CultureInfo.CurrentCulture, "Stacked {0:N0}", detail.FramesStacked));
        }

        if (detail.IntegrationMilliseconds is { } integration)
        {
            metadataHints.Add(string.Format(CultureInfo.CurrentCulture, "Integration {0:F2} s", integration / 1000d));
        }

        return new ImageHistoryFrameDetailViewModel(
            detail,
            captureLocal.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture),
            captureUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture),
            archivedLocal.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture),
            archivedUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture),
            rigSummary,
            cameraSummary,
            processingSummary,
            encodingSummary,
            detail.AppliedFilters,
            processedUri,
            processedDetailUri,
            processedDownload,
            rawDownload,
            thumbnailDownload,
            metadataHints);
    }

    public static string BuildMediaUri(
        NavigationManager navigationManager,
        Guid frameId,
        string variant,
        DateTimeOffset version,
        string? rawFormat = null)
    {
        ArgumentNullException.ThrowIfNull(navigationManager);

        var uri = FormattableString.Invariant($"api/v1.0/history/frames/{frameId:D}/media?variant={variant}");
        if (!string.IsNullOrWhiteSpace(rawFormat))
        {
            uri += string.Format(CultureInfo.InvariantCulture, "&rawFormat={0}", rawFormat);
        }

        uri += string.Format(CultureInfo.InvariantCulture, "&v={0}", version.UtcTicks);
        return navigationManager.ToAbsoluteUri(uri).ToString();
    }
}
