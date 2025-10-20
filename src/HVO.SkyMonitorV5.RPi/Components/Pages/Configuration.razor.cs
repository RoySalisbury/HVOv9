using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using HVO.SkyMonitorV5.RPi.Components.Shared;

namespace HVO.SkyMonitorV5.RPi.Components.Pages;

public partial class Configuration
{
    private const string DefaultTabKey = "overview";

    private static readonly IReadOnlyList<SkyMonitorTabDefinition> Tabs = new[]
    {
        new SkyMonitorTabDefinition("overview", "Overview", "/configuration", "bi bi-grid", NavLinkMatch.All),
    new SkyMonitorTabDefinition("system", "System", "/configuration?tab=system", "bi bi-cpu", NavLinkMatch.All),
    new SkyMonitorTabDefinition("rig", "Rig", "/configuration?tab=rig", "bi bi-diagram-3", NavLinkMatch.All),

    new SkyMonitorTabDefinition("cameras", "Cameras", "/configuration?tab=cameras", "bi bi-camera-video", NavLinkMatch.All),
        new SkyMonitorTabDefinition("optics", "Optics", "/configuration?tab=optics", "bi bi-binoculars", NavLinkMatch.All),
        new SkyMonitorTabDefinition("pipeline", "Pipeline", "/configuration?tab=pipeline", "bi bi-filter", NavLinkMatch.All, Disabled: true),
        new SkyMonitorTabDefinition("filters", "Filters", "/configuration?tab=filters", "bi bi-sliders", NavLinkMatch.All, Disabled: true)
    };

    [Parameter]
    [SupplyParameterFromQuery(Name = "tab")]
    public string? RequestedTabKey { get; set; }

    protected string ActiveTabKey { get; private set; } = DefaultTabKey;

    protected IReadOnlyList<SkyMonitorTabDefinition> ConfigurationTabs => Tabs;

    protected bool IsOverviewTabActive => string.Equals(ActiveTabKey, "overview", StringComparison.OrdinalIgnoreCase);

    protected bool IsSystemTabActive => string.Equals(ActiveTabKey, "system", StringComparison.OrdinalIgnoreCase);

    protected bool IsRigTabActive => string.Equals(ActiveTabKey, "rig", StringComparison.OrdinalIgnoreCase);



    protected bool IsCamerasTabActive => string.Equals(ActiveTabKey, "cameras", StringComparison.OrdinalIgnoreCase);

    protected bool IsOpticsTabActive => string.Equals(ActiveTabKey, "optics", StringComparison.OrdinalIgnoreCase);

    protected string ActiveTabDisplayName
        => Tabs.FirstOrDefault(tab => string.Equals(tab.Key, ActiveTabKey, StringComparison.OrdinalIgnoreCase))?.Label ?? "Configuration";

    protected override void OnParametersSet()
    {
        ActiveTabKey = ResolveActiveTabKey(RequestedTabKey);
    }

    private static string ResolveActiveTabKey(string? requestedKey)
    {
        if (string.IsNullOrWhiteSpace(requestedKey))
        {
            return DefaultTabKey;
        }

        foreach (var tab in Tabs)
        {
            if (string.Equals(tab.Key, requestedKey, StringComparison.OrdinalIgnoreCase))
            {
                return tab.Key;
            }
        }

        return DefaultTabKey;
    }
}
