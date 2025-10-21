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
        new SkyMonitorTabDefinition("overview", "Overview", "/diagnostics?tab=overview", "bi bi-activity", NavLinkMatch.All),
        new SkyMonitorTabDefinition("pipeline", "Pipeline", "/diagnostics?tab=pipeline", "bi bi-diagram-3", NavLinkMatch.All),
        new SkyMonitorTabDefinition("dispatch", "Dispatch", "/diagnostics?tab=dispatch", "bi bi-broadcast", NavLinkMatch.All),
        new SkyMonitorTabDefinition("exports", "Exports", "/diagnostics?tab=exports", "bi bi-cloud-upload", NavLinkMatch.All),
        new SkyMonitorTabDefinition("logs", "Logs", "/diagnostics?tab=logs", "bi bi-terminal", NavLinkMatch.All),
        new SkyMonitorTabDefinition("storage", "Storage", "/diagnostics?tab=storage", "bi bi-hdd-stack", NavLinkMatch.All)
    };
}
