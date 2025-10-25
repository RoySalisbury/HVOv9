using System;

namespace HVO.SkyMonitorV5.Data.Abstractions;

/// <summary>
/// Provides strongly-typed access to resolved data store locations.
/// </summary>
public interface ISkyMonitorDataPathProvider
{
    /// <summary>
    /// Gets the absolute root directory for all SkyMonitor data stores.
    /// </summary>
    string RootPath { get; }

    /// <summary>
    /// Resolves the absolute path for the specified relative location beneath the data root.
    /// </summary>
    /// <param name="relativePath">Relative path underneath the data root.</param>
    /// <returns>The absolute path for the requested location.</returns>
    /// <exception cref="ArgumentException">Thrown when the relative path is null, empty, or navigates above the root.</exception>
    string ResolvePath(string relativePath);
}
