using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Exports;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Services;
using HVO.SkyMonitorV5.RPi.Skia;
using HVO.SkyMonitorV5.RPi.Storage;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Components.Pages;

public sealed partial class RawFrameDetail : ComponentBase, IDisposable
{
    private RawFrameViewModel? _viewModel;
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
    private ILogger<RawFrameDetail> Logger { get; set; } = default!;

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

        var snapshot = FrameStateStore.LatestRawFrame;
        if (snapshot is null || snapshot.FrameId != frameId)
        {
            _errorMessage = "The requested raw frame is no longer available.";
            _isLoading = false;
            await RequestUiRefreshAsync();
            return;
        }

        var pngMedia = await FetchRawMediaAsync(frameId, snapshot.Timestamp, RawFrameMediaFormat.Png, cancellationToken);
        if (pngMedia is null || string.IsNullOrWhiteSpace(pngMedia.DataUri))
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                _errorMessage = "Unable to generate a display payload for this frame.";
                _isLoading = false;
                await RequestUiRefreshAsync();
            }

            return;
        }

        var rawMedia = await FetchRawMediaAsync(frameId, snapshot.Timestamp, RawFrameMediaFormat.Native, cancellationToken);

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var localTimestamp = ObservatoryClock.ToLocal(snapshot.Timestamp);
        var timestampStamp = localTimestamp.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        try
        {
            var descriptorEntries = BuildDescriptorEntries(snapshot, rawMedia?.Descriptor);

            _viewModel = new RawFrameViewModel(
                snapshot.FrameId,
                localTimestamp,
                pngMedia.DataUri,
                pngMedia.DataUri,
                rawMedia?.DataUri,
                FormattableString.Invariant($"raw-frame-{timestampStamp}.png"),
                FormattableString.Invariant($"raw-frame-{timestampStamp}.{SkiaRawFrameHelper.RawFileExtension}"),
                snapshot.Exposure.ExposureMilliseconds,
                snapshot.Exposure.Gain,
                snapshot.Exposure.AutoExposure,
                snapshot.Exposure.AutoGain,
                descriptorEntries);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to build raw frame detail view.");
            _errorMessage = "An unexpected error occurred while loading the raw frame.";
            _isLoading = false;
            await RequestUiRefreshAsync();
            return;
        }

        _isLoading = false;
        await RequestUiRefreshAsync();
    }

    private async Task<FrameMedia?> FetchRawMediaAsync(Guid frameId, DateTimeOffset timestamp, RawFrameMediaFormat format, CancellationToken cancellationToken)
    {
        try
        {
            return await FrameMediaProvider.GetRawFrameAsync(frameId, timestamp, format, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to retrieve {Format} raw frame via local API.", format);
            return null;
        }
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

    private static IReadOnlyList<DescriptorEntry> BuildDescriptorEntries(
        RawFrameSnapshot rawFrame,
        FrameExportImageDescriptor? descriptorOverride)
    {
        var entries = new List<DescriptorEntry>(5)
        {
            new DescriptorEntry(
                "Resolution",
                FormattableString.Invariant($"{rawFrame.Image.Width} × {rawFrame.Image.Height} px"))
        };

        var descriptor = descriptorOverride ?? rawFrame.ImageDescriptor;

        if (descriptor is not null)
        {
            if (!string.IsNullOrWhiteSpace(descriptor.PixelFormatHint))
            {
                entries.Add(new DescriptorEntry("Pixel format", descriptor.PixelFormatHint));
            }
            else
            {
                entries.Add(new DescriptorEntry("Pixel format", rawFrame.Image.ColorType.ToString()));
            }

            if (descriptor.RowBytes > 0)
            {
                entries.Add(new DescriptorEntry("Row bytes", FormattableString.Invariant($"{descriptor.RowBytes:N0}")));
            }

            if (descriptor.BytesPerPixel > 0)
            {
                entries.Add(new DescriptorEntry("Bytes / pixel", FormattableString.Invariant($"{descriptor.BytesPerPixel}")));
            }
        }
        else
        {
            entries.Add(new DescriptorEntry("Pixel format", rawFrame.Image.ColorType.ToString()));
        }

        return entries;
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

    private sealed record RawFrameViewModel(
        Guid FrameId,
        DateTimeOffset Timestamp,
        string ImageSource,
        string PngDownloadSource,
        string? RawDownloadSource,
        string PngDownloadFileName,
        string RawDownloadFileName,
        int ExposureMilliseconds,
        int Gain,
        bool AutoExposure,
        bool AutoGain,
        IReadOnlyList<DescriptorEntry> DescriptorEntries)
    {
        public string TimestampDisplay => Timestamp.ToString("MMM d, yyyy • h:mm:ss tt", CultureInfo.CurrentCulture);

        public string ExposureSummary => FormatIntegrationText(ExposureMilliseconds);

        public string GainSummary => FormattableString.Invariant($"Gain {Gain}");

        public string AutoSummary
        {
            get
            {
                var exposureLabel = AutoExposure ? "Auto" : "Manual";
                var gainLabel = AutoGain ? "Auto" : "Manual";
                return FormattableString.Invariant($"{exposureLabel} exposure · {gainLabel} gain");
            }
        }

        public bool HasRawDownload => !string.IsNullOrWhiteSpace(RawDownloadSource);
    }

    private sealed record DescriptorEntry(string Label, string Value);

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
}
