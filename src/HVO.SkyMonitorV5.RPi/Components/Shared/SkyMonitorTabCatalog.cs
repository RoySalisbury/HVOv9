using System.Collections.Generic;
using Microsoft.AspNetCore.Components.Routing;

namespace HVO.SkyMonitorV5.RPi.Components.Shared;

internal static class SkyMonitorTabCatalog
{
    public static IReadOnlyList<SkyMonitorTabDefinition> ImageHistoryTabs { get; } = new[]
    {
        new SkyMonitorTabDefinition("image-history-overview", "Overview", "/image-history", "bi bi-eye", NavLinkMatch.All)
    };

    public static IReadOnlyList<SkyMonitorTabDefinition> ConfigurationTabs { get; } = new[]
    {
        new SkyMonitorTabDefinition("overview", "Overview", "/configuration", "bi bi-grid", NavLinkMatch.All),
        new SkyMonitorTabDefinition("system", "System", "/configuration?tab=system", "bi bi-cpu", NavLinkMatch.All),
        new SkyMonitorTabDefinition("rig", "Rig", "/configuration?tab=rig", "bi bi-diagram-3", NavLinkMatch.All),
        new SkyMonitorTabDefinition("drivers", "Drivers", "/configuration?tab=drivers", "bi bi-hdd-network", NavLinkMatch.All),
        new SkyMonitorTabDefinition("cameras", "Cameras", "/configuration?tab=cameras", "bi bi-camera-video", NavLinkMatch.All),
        new SkyMonitorTabDefinition("optics", "Optics", "/configuration?tab=optics", "bi bi-binoculars", NavLinkMatch.All),
        new SkyMonitorTabDefinition("pipeline", "Pipeline", "/configuration?tab=pipeline", "bi bi-filter", NavLinkMatch.All, Disabled: true),
        new SkyMonitorTabDefinition("filters", "Filters", "/configuration?tab=filters", "bi bi-sliders", NavLinkMatch.All, Disabled: true)
    };

    public static IReadOnlyList<SkyMonitorTabDefinition> DiagnosticsTabs { get; } = new[]
    {
        new SkyMonitorTabDefinition("system", "System", "/diagnostics?tab=system", "bi bi-activity", NavLinkMatch.All),
        new SkyMonitorTabDefinition("filters", "Filters", "/diagnostics?tab=filters", "bi bi-funnel", NavLinkMatch.All),
        new SkyMonitorTabDefinition("queue", "Queue", "/diagnostics?tab=queue", "bi bi-stack", NavLinkMatch.All),
        new SkyMonitorTabDefinition("exports", "Exports", "/diagnostics?tab=exports", "bi bi-cloud-upload", NavLinkMatch.All, Disabled: true)
    };
}
