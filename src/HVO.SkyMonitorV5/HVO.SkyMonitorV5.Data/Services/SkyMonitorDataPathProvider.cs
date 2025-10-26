using System;
using System.IO;
using HVO.SkyMonitorV5.Data.Abstractions;
using HVO.SkyMonitorV5.Data.Options;
using Microsoft.Extensions.Options;

namespace HVO.SkyMonitorV5.Data.Services;

internal sealed class SkyMonitorDataPathProvider : ISkyMonitorDataPathProvider
{
    private readonly SkyMonitorDataRootOptions _options;

    public SkyMonitorDataPathProvider(IOptions<SkyMonitorDataRootOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value ?? throw new ArgumentException("SkyMonitor data root options are not configured.", nameof(options));
        RootPath = _options.ResolveRootPath();
    }

    public string RootPath { get; }

    public string ResolvePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Relative path must be supplied.", nameof(relativePath));
        }

        if (Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("Relative path cannot be rooted.", nameof(relativePath));
        }

        var candidate = Path.GetFullPath(Path.Combine(RootPath, relativePath));
        if (!candidate.StartsWith(RootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Resolved path navigates outside of the configured data root.");
        }

        var directory = Path.GetDirectoryName(candidate);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return candidate;
    }
}
