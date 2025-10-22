using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Models.ImageHistory;
using Microsoft.AspNetCore.Components;

namespace HVO.SkyMonitorV5.RPi.Components.Pages.Partials;

public sealed partial class ImageHistoryFilters : ComponentBase
{
    private static readonly IReadOnlyList<LookbackOption> LookbackOptionsInternal = new[]
    {
        new LookbackOption("last-6h", "Last 6 hours", TimeSpan.FromHours(6), "Recent frames from the past six hours."),
        new LookbackOption("last-12h", "Last 12 hours", TimeSpan.FromHours(12), "Standard half-day window."),
        new LookbackOption("last-24h", "Last 24 hours", TimeSpan.FromHours(24), "Full day of archive entries."),
        new LookbackOption("last-48h", "Last 48 hours", TimeSpan.FromHours(48), "Extended two-day history."),
    };

    private static readonly int[] PageSizesInternal = { 30, 60, 90, 120 };
    private FilterFormModel _model = FilterFormModel.FromState(ImageHistoryFilterState.CreateDefault(), LookbackOptionsInternal, PageSizesInternal);

    [Parameter]
    public ImageHistoryFilterState Filters { get; set; } = ImageHistoryFilterState.CreateDefault();

    [Parameter]
    public EventCallback<ImageHistoryFilterState> OnFiltersApplied { get; set; }

    [Parameter]
    public EventCallback OnRefreshRequested { get; set; }

    [Parameter]
    public bool IsBusy { get; set; }
    protected override void OnParametersSet()
    {
        _model = FilterFormModel.FromState(Filters, LookbackOptionsInternal, PageSizesInternal);
    }

    private IReadOnlyList<LookbackOption> LookbackOptions => LookbackOptionsInternal;

    private IReadOnlyList<int> PageSizeOptions => PageSizesInternal;

    private void SelectLookback(string optionKey)
    {
        if (IsBusy)
        {
            return;
        }

    _model.SelectedLookback = optionKey;
    }

    private async Task ApplyAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var option = LookbackOptionsInternal.FirstOrDefault(o => o.Key == _model.SelectedLookback) ?? LookbackOptionsInternal[0];
        var pageSize = PageSizesInternal.Contains(_model.PageSize) ? _model.PageSize : PageSizesInternal[1];

        var state = new ImageHistoryFilterState(
            option.Duration,
            Normalize(_model.Rig),
            Normalize(_model.Camera),
            pageSize);

        await OnFiltersApplied.InvokeAsync(state).ConfigureAwait(false);
    }

    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        await OnRefreshRequested.InvokeAsync().ConfigureAwait(false);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record LookbackOption(string Key, string Label, TimeSpan Duration, string Description);

    private sealed class FilterFormModel
    {
        public FilterFormModel(string selectedLookback, string? rig, string? camera, int pageSize)
        {
            SelectedLookback = selectedLookback;
            Rig = rig;
            Camera = camera;
            PageSize = pageSize;
        }

        public string SelectedLookback { get; set; }

        public string? Rig { get; set; }

        public string? Camera { get; set; }

        public int PageSize { get; set; }

        public static FilterFormModel FromState(ImageHistoryFilterState state, IReadOnlyList<LookbackOption> options, IReadOnlyList<int> pageSizes)
        {
            if (options.Count == 0)
            {
                return new FilterFormModel("last-12h", state.RigName, state.CameraName, state.PageSize);
            }

            var lookbackKey = options.FirstOrDefault(option => Math.Abs(option.Duration.TotalHours - state.Lookback.TotalHours) < 0.1)?.Key
                              ?? options[0].Key;

            var fallbackPageSize = pageSizes.Contains(60) ? 60 : (pageSizes.Count > 0 ? pageSizes[0] : state.PageSize);
            var pageSize = pageSizes.Contains(state.PageSize) ? state.PageSize : fallbackPageSize;
            return new FilterFormModel(lookbackKey, state.RigName, state.CameraName, pageSize);
        }
    }
}
