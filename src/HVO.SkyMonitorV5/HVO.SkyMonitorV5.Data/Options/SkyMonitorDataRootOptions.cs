using System;
using System.IO;
using System.Runtime.InteropServices;

namespace HVO.SkyMonitorV5.Data.Options;

/// <summary>
/// Defines how the SkyMonitor data store root should be resolved on the current host.
/// </summary>
public sealed class SkyMonitorDataRootOptions
{
    public const string SectionName = "SkyMonitor:Data";

    /// <summary>
    /// Optional explicit override for the data root directory.
    /// </summary>
    public string? OverrideRootPath { get; set; }

    /// <summary>
    /// Default root location used when running locally (outside a container).
    /// </summary>
    public string DefaultLocalRoot { get; set; } = Path.Combine(AppContext.BaseDirectory, "Data");

    /// <summary>
    /// Default root location used when running inside a container environment.
    /// </summary>
    public string ContainerRoot { get; set; } = "/var/hvo/datastores";

    /// <summary>
    /// When true, prefer the <see cref="ContainerRoot"/> even if <see cref="OverrideRootPath"/> is not set.
    /// </summary>
    public bool PreferContainerRoot { get; set; } = OperatingSystem.IsLinux() &&
        string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves the effective root path respecting overrides and platform defaults.
    /// </summary>
    public string ResolveRootPath()
    {
        if (!string.IsNullOrWhiteSpace(OverrideRootPath))
        {
            return Normalize(OverrideRootPath!);
        }

        if (PreferContainerRoot)
        {
            return Normalize(ContainerRoot);
        }

        return Normalize(DefaultLocalRoot);
    }

    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("The supplied data root path is empty.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }
}
