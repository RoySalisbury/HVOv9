using System;

namespace HVO.SkyMonitorV5.RPi.Exports;

/// <summary>
/// Shared helpers for constructing deterministic export file names and resolving timestamps.
/// </summary>
internal static class FrameExportPathUtilities
{
    public static string BuildBaseFileName(DateTimeOffset timestampUtc, Guid frameId)
        => FormattableString.Invariant($"{timestampUtc:HHmmssfff}-{frameId:N}");

    public static string ResolveExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return "bin";
        }

        var trimmed = extension.Trim();
        if (trimmed.Length > 0 && trimmed[0] == '.')
        {
            trimmed = trimmed[1..];
        }

        return trimmed.Length == 0 ? "bin" : trimmed;
    }

    public static DateTimeOffset ResolveStageTimestamp(FrameExportMetadata metadata)
    {
        if (metadata is null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        if (metadata.StageTimestampUtc != default)
        {
            return metadata.StageTimestampUtc;
        }

        if (metadata.CapturedAtUtc != default)
        {
            return metadata.CapturedAtUtc;
        }

        return DateTimeOffset.UtcNow;
    }
}
