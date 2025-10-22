using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Components.Shared;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Models.ImageHistory;
using HVO.SkyMonitorV5.RPi.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Components.Pages;

public sealed partial class ImageHistory : ComponentBase, IDisposable
{
    private readonly List<ImageHistoryThumbnailViewModel> _thumbnails = new();
    private readonly List<ImageHistoryChartSeriesViewModel> _chartSeries = new();
    private ImageHistoryFilterState _filters = ImageHistoryFilterState.CreateDefault();
    private ImageHistoryFrameDetailViewModel? _selectedDetail;
    private Guid? _selectedFrameId;
    private string? _nextCursor;
    private bool _isLoadingThumbnails;
    private bool _isLoadingDetail;
    private string? _thumbnailError;
    private string? _detailError;
    private CancellationTokenSource? _loadCts;
    private bool _disposed;

    [Inject]
    private IImageHistoryService ImageHistoryService { get; set; } = default!;

    [Inject]
    private IObservatoryClock ObservatoryClock { get; set; } = default!;

    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private ILogger<ImageHistory> Logger { get; set; } = default!;

    private IReadOnlyList<SkyMonitorTabDefinition> HistoryTabs => SkyMonitorTabCatalog.ImageHistoryTabs;

    private string ActiveTabKey => "image-history-overview";

    private IReadOnlyList<ImageHistoryThumbnailViewModel> ThumbnailItems => _thumbnails;

    private IReadOnlyList<ImageHistoryChartSeriesViewModel> ChartSeries => _chartSeries;

    private bool IsBusy => _isLoadingThumbnails || _isLoadingDetail;

    protected override async Task OnInitializedAsync()
    {
        await LoadThumbnailsAsync(reset: true).ConfigureAwait(false);
    }

    private async Task HandleFiltersAppliedAsync(ImageHistoryFilterState filters)
    {
        _filters = filters;
        await LoadThumbnailsAsync(reset: true).ConfigureAwait(false);
    }

    private async Task HandleRefreshRequestedAsync()
    {
        await LoadThumbnailsAsync(reset: true).ConfigureAwait(false);
    }

    private async Task HandleThumbnailSelectedAsync(Guid frameId)
    {
        if (_isLoadingDetail || frameId == Guid.Empty)
        {
            return;
        }

        if (_selectedFrameId == frameId && _selectedDetail is not null)
        {
            return;
        }

        _selectedFrameId = frameId;
        await LoadDetailAsync(frameId).ConfigureAwait(false);
    }

    private async Task LoadThumbnailsAsync(bool reset)
    {
        CancelOutstandingLoad();
        _loadCts = new CancellationTokenSource();
        var cancellationToken = _loadCts.Token;

        if (reset)
        {
            _thumbnails.Clear();
            _chartSeries.Clear();
            _nextCursor = null;
            _selectedDetail = null;
            _selectedFrameId = null;
            _thumbnailError = null;
            _detailError = null;
        }

        _isLoadingThumbnails = true;
        await InvokeAsync(StateHasChanged).ConfigureAwait(false);

        try
        {
            var untilUtc = ObservatoryClock.UtcNow;
            var sinceUtc = untilUtc - _filters.Lookback;
            var request = new ImageHistoryThumbnailsRequest(
                sinceUtc,
                untilUtc,
                _filters.RigName,
                _filters.CameraName,
                _filters.PageSize,
                reset ? null : _nextCursor);

            var result = await ImageHistoryService.GetThumbnailsAsync(request, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccessful)
            {
                _thumbnailError = result.Error?.Message ?? "Unknown error while loading thumbnails.";
                Logger.LogWarning(result.Error, "Image history thumbnail request failed.");
                return;
            }

            var page = result.Value;
            if (reset)
            {
                _thumbnails.Clear();
            }

            foreach (var entry in page.Items)
            {
                var viewModel = BuildThumbnailViewModel(entry);
                _thumbnails.Add(viewModel);
            }

            _nextCursor = page.NextCursor;
            _thumbnailError = null;
            BuildChartSeries();

            if (reset && _thumbnails.Count > 0)
            {
                var first = _thumbnails[0];
                _selectedFrameId = first.Entry.FrameId;
                await LoadDetailAsync(first.Entry.FrameId).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during cancellation.
        }
        catch (Exception ex)
        {
            _thumbnailError = ex.Message;
            Logger.LogError(ex, "Failed to load image history thumbnails.");
        }
        finally
        {
            _isLoadingThumbnails = false;
            await InvokeAsync(StateHasChanged).ConfigureAwait(false);
        }
    }

    private async Task LoadDetailAsync(Guid frameId)
    {
        _isLoadingDetail = true;
        _detailError = null;
        await InvokeAsync(StateHasChanged).ConfigureAwait(false);

        try
        {
            var result = await ImageHistoryService.GetFrameAsync(frameId, CancellationToken.None).ConfigureAwait(false);
            if (!result.IsSuccessful)
            {
                _detailError = result.Error?.Message ?? "Unable to load image detail.";
                Logger.LogWarning(result.Error, "Image history detail request failed for frame {FrameId}.", frameId);
                _selectedDetail = null;
                return;
            }

            _selectedDetail = BuildDetailViewModel(result.Value);
        }
        catch (Exception ex)
        {
            _detailError = ex.Message;
            Logger.LogError(ex, "Failed to load image history detail for frame {FrameId}.", frameId);
            _selectedDetail = null;
        }
        finally
        {
            _isLoadingDetail = false;
            await InvokeAsync(StateHasChanged).ConfigureAwait(false);
        }
    }

    private ImageHistoryThumbnailViewModel BuildThumbnailViewModel(ImageHistoryThumbnailEntry entry)
    {
    var capturedLocal = ObservatoryClock.ToLocal(entry.CapturedAtUtc);
    var captureLabel = capturedLocal.ToString("HH:mm:ss", CultureInfo.CurrentCulture);
        var groupKey = capturedLocal.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var groupLabel = capturedLocal.ToString("MMMM dd, yyyy", CultureInfo.CurrentCulture);
    var subtitle = capturedLocal.ToString("MMM dd, yyyy", CultureInfo.CurrentCulture);

        var integrationSegment = entry.IntegrationMilliseconds is { } ms
            ? string.Format(CultureInfo.CurrentCulture, " · {0:F1}s", ms / 1000d)
            : string.Empty;

    var metadataSummary = string.Format(CultureInfo.CurrentCulture, "Stacked {0:N0}{1}", entry.FramesStacked, integrationSegment);
    var thumbnailUri = ImageHistoryViewModelMapper.BuildMediaUri(NavigationManager, entry.FrameId, "thumbnail", entry.ArchivedAtUtc);

        return new ImageHistoryThumbnailViewModel(
            entry,
            capturedLocal,
            captureLabel,
            groupKey,
            groupLabel,
            subtitle,
            metadataSummary,
            thumbnailUri);
    }

    private ImageHistoryFrameDetailViewModel BuildDetailViewModel(ImageHistoryFrameDetailResult result)
        => ImageHistoryViewModelMapper.CreateDetailViewModel(result, NavigationManager);

    private void BuildChartSeries()
    {
        _chartSeries.Clear();
        if (_thumbnails.Count == 0)
        {
            return;
        }

        var queueSeries = BuildLatencySeries(
            "Queue latency",
            _thumbnails,
            entry => entry.Entry.QueueLatencyMilliseconds);

        if (queueSeries is not null)
        {
            _chartSeries.Add(queueSeries);
        }

        var processingSeries = BuildLatencySeries(
            "Processing latency",
            _thumbnails,
            entry => entry.Entry.ProcessingMilliseconds,
            color: "var(--hvo-accent)"
        );

        if (processingSeries is not null)
        {
            _chartSeries.Add(processingSeries);
        }

        var pipelineSeries = BuildLatencySeries(
            "Full pipeline",
            _thumbnails,
            entry => entry.Entry.FullPipelineMilliseconds,
            color: "#ff7f50");

        if (pipelineSeries is not null)
        {
            _chartSeries.Add(pipelineSeries);
        }
    }

    private static ImageHistoryChartSeriesViewModel? BuildLatencySeries(
        string title,
        IEnumerable<ImageHistoryThumbnailViewModel> items,
        Func<ImageHistoryThumbnailViewModel, double?> selector,
        string? color = null)
    {
        var values = items
            .OrderBy(static i => i.Entry.CapturedAtUtc)
            .Select(selector)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .TakeLast(120)
            .ToList();

        if (values.Count == 0)
        {
            return null;
        }

        return new ImageHistoryChartSeriesViewModel(
            title,
            values,
            color ?? "#4dabf7",
            "Milliseconds",
            " ms");
    }

    private void CancelOutstandingLoad()
    {
        try
        {
            _loadCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelOutstandingLoad();
        _loadCts?.Dispose();
    }
}
