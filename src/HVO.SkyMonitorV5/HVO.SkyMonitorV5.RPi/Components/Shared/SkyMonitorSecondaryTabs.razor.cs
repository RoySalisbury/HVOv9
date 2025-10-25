using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace HVO.SkyMonitorV5.RPi.Components.Shared;

public partial class SkyMonitorSecondaryTabs : ComponentBase
{
    [Parameter]
    public IReadOnlyList<SkyMonitorTabDefinition> Tabs { get; set; } = Array.Empty<SkyMonitorTabDefinition>();

    [Parameter]
    public string? Label { get; set; }

    [Parameter]
    public string? ActiveTabKey { get; set; }

    private string ResolvedLabel => string.IsNullOrWhiteSpace(Label) ? "Secondary navigation" : Label!;

    private bool IsActive(SkyMonitorTabDefinition tab)
        => !string.IsNullOrWhiteSpace(ActiveTabKey) && string.Equals(tab.Key, ActiveTabKey, StringComparison.OrdinalIgnoreCase);

    private static string? GetActiveFlag(bool isActive) => isActive ? "true" : null;

    private static string GetTabCss(SkyMonitorTabDefinition tab, bool isActive)
    {
        var css = "hvo-tab-row__tab";

        if (tab.Disabled)
        {
            css += " hvo-tab-row__tab--disabled";
        }

        if (isActive)
        {
            css += " hvo-tab-row__tab--active";
        }

        return css;
    }
}

public sealed record SkyMonitorTabDefinition(
    string Key,
    string Label,
    string Href,
    string? IconCssClass = null,
    NavLinkMatch Match = NavLinkMatch.Prefix,
    bool Disabled = false);
