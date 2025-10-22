using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Models.ImageHistory;
using Microsoft.AspNetCore.Components;

namespace HVO.SkyMonitorV5.RPi.Components.Pages.Partials;

public sealed partial class ImageHistoryRail : ComponentBase
{
    private IReadOnlyList<RailGroup> _groups = Array.Empty<RailGroup>();

    [Parameter]
    public IReadOnlyList<ImageHistoryThumbnailViewModel> Items { get; set; } = Array.Empty<ImageHistoryThumbnailViewModel>();

    [Parameter]
    public Guid? SelectedFrameId { get; set; }

    [Parameter]
    public bool IsLoading { get; set; }

    [Parameter]
    public string? ErrorMessage { get; set; }

    [Parameter]
    public EventCallback<Guid> OnSelectItem { get; set; }

    [Parameter]
    public EventCallback OnRefreshRequested { get; set; }

    private IReadOnlyList<RailGroup> Groups => _groups;

    private string ThumbnailCountLabel => Items.Count == 1 ? "1 frame" : $"{Items.Count:N0} frames";

    protected override void OnParametersSet()
    {
        _groups = BuildGroups(Items);
    }

    private static IReadOnlyList<RailGroup> BuildGroups(IReadOnlyList<ImageHistoryThumbnailViewModel> items)
    {
        if (items.Count == 0)
        {
            return Array.Empty<RailGroup>();
        }

        var groups = new List<RailGroupBuilder>();
        RailGroupBuilder? current = null;

        foreach (var item in items)
        {
            if (current is null || !string.Equals(current.Key, item.GroupKey, StringComparison.Ordinal))
            {
                current = new RailGroupBuilder(item.GroupKey, item.GroupLabel);
                groups.Add(current);
            }

            current.Items.Add(item);
        }

        if (groups.Count == 0)
        {
            return Array.Empty<RailGroup>();
        }

        return groups.Select(static builder => builder.ToGroup()).ToList();
    }

    private string BuildThumbnailAlt(ImageHistoryThumbnailViewModel item)
    {
        var caption = string.IsNullOrWhiteSpace(item.Subtitle)
            ? item.CaptureLabel
            : $"{item.CaptureLabel}, {item.Subtitle}";

        return string.IsNullOrWhiteSpace(caption)
            ? "Sky monitor frame"
            : caption;
    }

    private async Task SelectAsync(Guid frameId)
    {
        if (OnSelectItem.HasDelegate)
        {
            await OnSelectItem.InvokeAsync(frameId).ConfigureAwait(false);
        }
    }

    private async Task RefreshAsync()
    {
        if (OnRefreshRequested.HasDelegate)
        {
            await OnRefreshRequested.InvokeAsync().ConfigureAwait(false);
        }
    }

    private readonly record struct RailGroup(string Label, IReadOnlyList<ImageHistoryThumbnailViewModel> Items);

    private sealed class RailGroupBuilder
    {
        public RailGroupBuilder(string key, string label)
        {
            Key = key;
            Label = label;
        }

        public string Key { get; }

        public string Label { get; }

        public List<ImageHistoryThumbnailViewModel> Items { get; } = [];

        public RailGroup ToGroup() => new(Label, Items);
    }
}
