using System.Collections.Generic;
using System.Globalization;
using HVO.SkyMonitorV5.RPi.Models.ImageHistory;
using Microsoft.AspNetCore.Components;

namespace HVO.SkyMonitorV5.RPi.Components.Pages.Partials;

public sealed partial class ImageHistoryDetail : ComponentBase
{
    private IReadOnlyList<DownloadLink> _downloadLinks = Array.Empty<DownloadLink>();

    [Parameter]
    public ImageHistoryFrameDetailViewModel? Detail { get; set; }

    [Parameter]
    public IReadOnlyList<ImageHistoryChartSeriesViewModel> Charts { get; set; } = Array.Empty<ImageHistoryChartSeriesViewModel>();

    [Parameter]
    public bool IsLoading { get; set; }

    [Parameter]
    public string? ErrorMessage { get; set; }

    private string HeaderSubtitle => Detail is null
        ? "Waiting for frame selection"
        : string.Format(CultureInfo.CurrentCulture, "Captured {0}", Detail.CaptureLocalDisplay);

    private string FrameSummaryBadge => Detail is null
        ? string.Empty
        : string.Format(CultureInfo.CurrentCulture, "Stacked {0:N0} frames", Detail.Detail.FramesStacked);

    private IReadOnlyList<DownloadLink> DownloadLinks => _downloadLinks;

    private IReadOnlyList<ImageHistoryChartSeriesViewModel> ChartSeries => Charts ?? Array.Empty<ImageHistoryChartSeriesViewModel>();

    protected override void OnParametersSet()
    {
        _downloadLinks = BuildDownloadLinks(Detail);
    }

    private static IReadOnlyList<DownloadLink> BuildDownloadLinks(ImageHistoryFrameDetailViewModel? detail)
    {
        if (detail is null)
        {
            return Array.Empty<DownloadLink>();
        }

        var links = new List<DownloadLink>(capacity: 4);

        if (!string.IsNullOrWhiteSpace(detail.ProcessedDetailUri))
        {
            links.Add(new("View processed", detail.ProcessedDetailUri, false, false));
        }

        if (!string.IsNullOrWhiteSpace(detail.ProcessedDownloadUri))
        {
            links.Add(new("Download processed", detail.ProcessedDownloadUri, true, true));
        }

        if (!string.IsNullOrWhiteSpace(detail.RawDownloadUri))
        {
            links.Add(new("Download raw", detail.RawDownloadUri, true, true));
        }

        if (!string.IsNullOrWhiteSpace(detail.ThumbnailDownloadUri))
        {
            links.Add(new("Download thumbnail", detail.ThumbnailDownloadUri, true, true));
        }

        return links;
    }

    private static string BuildPreviewAlt(ImageHistoryFrameDetailViewModel detail)
    {
        return string.Format(
            CultureInfo.CurrentCulture,
            "Sky monitor frame captured {0} using {1}",
            detail.CaptureLocalDisplay,
            detail.CameraSummary);
    }

    private static string? GetDownloadValue(DownloadLink link)
        => link.ForceDownload ? string.Empty : null;

    private sealed record DownloadLink(string Label, string Uri, bool ForceDownload, bool OpenInNewTab);
}
