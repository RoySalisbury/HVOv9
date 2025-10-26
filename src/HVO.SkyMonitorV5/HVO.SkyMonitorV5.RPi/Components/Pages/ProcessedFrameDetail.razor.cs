using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Pipeline.Composition;
using HVO.SkyMonitorV5.RPi.Services;
using HVO.SkyMonitorV5.RPi.Skia;
using HVO.SkyMonitorV5.RPi.Storage;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Components.Pages;

public sealed partial class ProcessedFrameDetail : ComponentBase, IDisposable
{
    private ProcessedFrameViewModel? _viewModel;
    private bool _isLoading = true;
    private string? _errorMessage;
    private string? _currentFrameId;
    private CancellationTokenSource? _loadCts;

    [Parameter]
    [SupplyParameterFromQuery(Name = "frameId")]
    public string? FrameId { get; set; }

    [Parameter]
    [SupplyParameterFromQuery(Name = "datetime")]
    public string? Timestamp { get; set; }

    [Parameter]
    [SupplyParameterFromQuery(Name = "type")]
    public string? RequestedType { get; set; }

    [Inject]
    private IFrameStateStore FrameStateStore { get; set; } = default!;

    [Inject]
    private IObservatoryClock ObservatoryClock { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private ILogger<ProcessedFrameDetail> Logger { get; set; } = default!;

    [Inject]
    private IProcessedFrameEncoder ProcessedFrameEncoder { get; set; } = default!;

    [Inject]
    private IFrameMediaProvider FrameMediaProvider { get; set; } = default!;

    protected override void OnParametersSet()
    {
        if (string.IsNullOrWhiteSpace(FrameId))
        {
            if (_viewModel is null)
            {
                _errorMessage = "The frame identifier is missing or invalid.";
                _isLoading = false;
            }

            return;
        }

        if (string.Equals(_currentFrameId, FrameId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _currentFrameId = FrameId;

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();

        _ = LoadFrameAsync(FrameId, _loadCts.Token);
    }

    private async Task LoadFrameAsync(string frameIdValue, CancellationToken cancellationToken)
    {
        _isLoading = true;
        _errorMessage = null;
        _viewModel = null;

        if (!Guid.TryParse(frameIdValue, out var frameId))
        {
            _errorMessage = "The frame identifier is missing or invalid.";
            _isLoading = false;
            await RequestUiRefreshAsync();
            return;
        }

        var history = FrameStateStore.GetComposedFrameHistory();
        ComposedFrame? composedFrame = history.FirstOrDefault(frame => frame.FrameId == frameId);

        if (composedFrame is not { } composed)
        {
            var latest = FrameStateStore.LatestProcessedFrame;
            if (latest is null || latest.FrameId != frameId)
            {
                _errorMessage = "The requested processed frame is no longer available.";
                _isLoading = false;
                await RequestUiRefreshAsync();
                return;
            }

            await CreateViewModelFromProcessedAsync(latest, cancellationToken);
        }
        else
        {
            await CreateViewModelFromComposedAsync(composed, cancellationToken);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        _isLoading = false;
        await RequestUiRefreshAsync();
    }

    private Task CreateViewModelFromComposedAsync(ComposedFrame composed, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        SKImage? clone = null;
        try
        {
            clone = SkiaImageUtilities.CloneToRaster(composed.Image) ?? composed.Image;
            using var encoded = clone.Encode(SKEncodedImageFormat.Png, 95);
            if (encoded is null || encoded.Size == 0)
            {
                _errorMessage = "Unable to encode the processed frame image.";
                return Task.CompletedTask;
            }

            var bytes = encoded.ToArray();
            var dataUri = FrameViewUtilities.BuildDataUri(bytes, "image/png");
            if (dataUri is null)
            {
                _errorMessage = "Unable to generate a display payload for this frame.";
                return Task.CompletedTask;
            }

            var localTimestamp = ObservatoryClock.ToLocal(composed.Timestamp);

            var filterDisplays = BuildFilterDisplays(composed.AppliedFilters, composed.FilterExecutions);

            _viewModel = new ProcessedFrameViewModel(
                composed.FrameId,
                localTimestamp,
                dataUri,
                "png",
                composed.IntegrationMilliseconds,
                composed.FramesStacked,
                composed.SurfaceMilliseconds,
                filterDisplays);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to build processed frame detail view.");
            _errorMessage = "An unexpected error occurred while loading the processed frame.";
        }
        finally
        {
            if (!ReferenceEquals(clone, composed.Image))
            {
                clone?.Dispose();
            }
        }

        return Task.CompletedTask;
    }

    private async Task CreateViewModelFromProcessedAsync(ProcessedFrame processed, CancellationToken cancellationToken)
    {
        try
        {
            FrameMedia? media = null;

            try
            {
                media = await FrameMediaProvider.GetProcessedFrameAsync(processed.FrameId, processed.Timestamp, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to retrieve processed frame via local API. Falling back to frame buffer.");
            }

            if (media is null)
            {
                var delivery = ProcessedFrameEncoder.Encode(processed);
                var payload = delivery.Payload.ToArray();
                var contentType = string.IsNullOrWhiteSpace(delivery.ContentType) ? "image/png" : delivery.ContentType;
                var dataUri = FrameViewUtilities.BuildDataUri(payload, contentType);
                if (dataUri is null)
                {
                    _errorMessage = "Unable to generate a display payload for this frame.";
                    return;
                }

                media = new FrameMedia(
                    processed.FrameId,
                    processed.Timestamp,
                    contentType,
                    delivery.FileExtension ?? processed.FileExtension ?? "png",
                    dataUri,
                    payload);
            }

            if (media is null)
            {
                _errorMessage = "No display payload was available for the requested frame.";
                return;
            }

            if (string.IsNullOrWhiteSpace(media.DataUri))
            {
                _errorMessage = "No display payload was available for the requested frame.";
                return;
            }

            var localTimestamp = ObservatoryClock.ToLocal(media.Timestamp);
            var filterDisplays = BuildFilterDisplays(processed.AppliedFilters, processed.FilterExecutions);

            _viewModel = new ProcessedFrameViewModel(
                processed.FrameId,
                localTimestamp,
                media.DataUri,
                media.FileExtension,
                processed.IntegrationMilliseconds,
                processed.FramesStacked,
                processed.SurfaceMilliseconds,
                filterDisplays);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to encode processed frame detail payload.");
            _errorMessage = "An unexpected error occurred while loading the processed frame.";
        }
    }

    public void Dispose()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
    }

    private void NavigateBack()
    {
        Navigation.NavigateTo("/monitor", forceLoad: false, replace: false);
    }

    private sealed record ProcessedFrameViewModel(
        Guid FrameId,
        DateTimeOffset Timestamp,
        string ImageSource,
        string DownloadExtension,
        int IntegrationMilliseconds,
        int FramesStacked,
        double SurfaceMilliseconds,
        IReadOnlyList<FilterDisplay> Filters)
    {
        public string TimestampDisplay => Timestamp.ToString("MMM d, yyyy • h:mm:ss tt", CultureInfo.CurrentCulture);

        public string IntegrationSummary => FormatIntegrationText(IntegrationMilliseconds);

        public string FramesSummary => FramesStacked == 1
            ? "Single frame"
            : FormattableString.Invariant($"{FramesStacked} frames");

        public string SurfaceSummary => SurfaceMilliseconds <= 0
            ? "<1 ms"
            : FormattableString.Invariant($"{SurfaceMilliseconds:0.0} ms");

        public string DownloadFileName => FormattableString.Invariant($"processed-frame-{Timestamp:yyyyMMdd-HHmmss}.{DownloadExtension}");

        public bool HasFilters => Filters.Count > 0;
    }

    private sealed record FilterDisplay(string Name, string? DurationDisplay);

    private static IReadOnlyList<FilterDisplay> BuildFilterDisplays(
        IReadOnlyList<string>? filters,
        IReadOnlyList<FilterExecution>? executions)
    {
        if ((filters is null || filters.Count == 0) && (executions is null || executions.Count == 0))
        {
            return Array.Empty<FilterDisplay>();
        }

        var displays = new List<FilterDisplay>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (filters is { Count: > 0 })
        {
            foreach (var filter in filters)
            {
                var duration = TryFormatDuration(executions, filter);
                displays.Add(new FilterDisplay(filter, duration));
                seen.Add(filter);
            }
        }

        if (executions is { Count: > 0 })
        {
            foreach (var execution in executions)
            {
                if (seen.Add(execution.FilterName))
                {
                    var formattedDuration = FormatDuration(execution.DurationMilliseconds);
                    displays.Add(new FilterDisplay(execution.FilterName, formattedDuration));
                }
            }
        }

        return displays;
    }

    private static string? TryFormatDuration(IReadOnlyList<FilterExecution>? executions, string filterName)
    {
        if (executions is not { Count: > 0 })
        {
            return null;
        }

        foreach (var execution in executions)
        {
            if (string.Equals(execution.FilterName, filterName, StringComparison.OrdinalIgnoreCase))
            {
                return FormatDuration(execution.DurationMilliseconds);
            }
        }

        return null;
    }

    private static string? FormatDuration(double milliseconds) => milliseconds <= 0
        ? null
        : FormattableString.Invariant($"{milliseconds:0.0} ms");

    private static string FormatIntegrationText(int integrationMilliseconds)
    {
        if (integrationMilliseconds <= 0)
        {
            return "0 ms";
        }

        if (integrationMilliseconds < 1_000)
        {
            return FormattableString.Invariant($"{integrationMilliseconds} ms");
        }

        var seconds = integrationMilliseconds / 1_000d;
        if (seconds < 60)
        {
            return FormattableString.Invariant($"{seconds:0.0} s");
        }

        var minutes = seconds / 60d;
        if (minutes < 60)
        {
            return FormattableString.Invariant($"{minutes:0.0} min");
        }

        var hours = minutes / 60d;
        return FormattableString.Invariant($"{hours:0.0} hr");
    }

    private async Task RequestUiRefreshAsync()
    {
        try
        {
            await InvokeAsync(StateHasChanged);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }
}
