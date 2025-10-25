using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Components;
using HVO.SkyMonitorV5.RPi.Components.Shared;

namespace HVO.SkyMonitorV5.RPi.Components.Pages;

public partial class Configuration
{
    private const string DefaultTabKey = "overview";

    [Parameter]
    [SupplyParameterFromQuery(Name = "tab")]
    public string? RequestedTabKey { get; set; }

    protected string ActiveTabKey { get; private set; } = DefaultTabKey;

    protected IReadOnlyList<SkyMonitorTabDefinition> ConfigurationTabs => SkyMonitorTabCatalog.ConfigurationTabs;

    protected bool IsOverviewTabActive => string.Equals(ActiveTabKey, "overview", StringComparison.OrdinalIgnoreCase);

    protected bool IsSystemTabActive => string.Equals(ActiveTabKey, "system", StringComparison.OrdinalIgnoreCase);

    protected bool IsRigTabActive => string.Equals(ActiveTabKey, "rig", StringComparison.OrdinalIgnoreCase);

    protected bool IsDriversTabActive => string.Equals(ActiveTabKey, "drivers", StringComparison.OrdinalIgnoreCase);

    protected bool IsCamerasTabActive => string.Equals(ActiveTabKey, "cameras", StringComparison.OrdinalIgnoreCase);

    protected bool IsOpticsTabActive => string.Equals(ActiveTabKey, "optics", StringComparison.OrdinalIgnoreCase);

    protected string ActiveTabDisplayName
        => ConfigurationTabs.FirstOrDefault(tab => string.Equals(tab.Key, ActiveTabKey, StringComparison.OrdinalIgnoreCase))?.Label ?? "Configuration";

    protected override void OnParametersSet()
    {
        ActiveTabKey = ResolveActiveTabKey(RequestedTabKey);
    }

    private string ResolveActiveTabKey(string? requestedKey)
    {
        if (string.IsNullOrWhiteSpace(requestedKey))
        {
            return DefaultTabKey;
        }

        foreach (var tab in ConfigurationTabs)
        {
            if (string.Equals(tab.Key, requestedKey, StringComparison.OrdinalIgnoreCase))
            {
                return tab.Key;
            }
        }

        return DefaultTabKey;
    }
}
