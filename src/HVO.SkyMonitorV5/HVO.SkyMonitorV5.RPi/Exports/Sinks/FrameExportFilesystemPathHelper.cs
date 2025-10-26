using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HVO.SkyMonitorV5.RPi.Options;

namespace HVO.SkyMonitorV5.RPi.Exports.Sinks;

/// <summary>
/// Helper utilities for computing filesystem export paths that mirror the sink layout.
/// </summary>
internal static class FrameExportFilesystemPathHelper
{
    public static string BuildPayloadPath(
        FilesystemFrameExportSinkOptions configuration,
        FrameExportPayloadRole role,
        DateTimeOffset timestampUtc,
        Guid frameId,
        string? fileExtension)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var segments = BuildPathSegments(configuration, role, timestampUtc);
        var directory = Path.Combine(segments);
        var baseName = FrameExportPathUtilities.BuildBaseFileName(timestampUtc, frameId);
        var extension = FrameExportPathUtilities.ResolveExtension(fileExtension);
        return Path.Combine(directory, FormattableString.Invariant($"{baseName}.{extension}"));
    }

    public static string[] BuildPathSegments(
        FilesystemFrameExportSinkOptions configuration,
        FrameExportPayloadRole role,
        DateTimeOffset timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var segments = new List<string>(8)
        {
            configuration.RootPath ?? string.Empty
        };

        var prefixSegments = configuration.EnumeratePrefixSegments();
        foreach (var segment in prefixSegments)
        {
            segments.Add(segment);
        }

        var roleDirectory = GetRoleDirectoryName(role);
        if (segments.Count == 0 || !string.Equals(segments[^1], roleDirectory, StringComparison.OrdinalIgnoreCase))
        {
            segments.Add(roleDirectory);
        }

        segments.Add(timestampUtc.ToString("yyyy", CultureInfo.InvariantCulture));
        segments.Add(timestampUtc.ToString("MM", CultureInfo.InvariantCulture));
        segments.Add(timestampUtc.ToString("dd", CultureInfo.InvariantCulture));

        return segments.ToArray();
    }

    public static string GetRoleDirectoryName(FrameExportPayloadRole role) => role switch
    {
        FrameExportPayloadRole.Archive => "archive",
        FrameExportPayloadRole.Delivery => "delivery",
        _ => "unknown"
    };
}
