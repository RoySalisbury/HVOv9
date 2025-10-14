using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using HVO.SkyMonitorV5.RPi.Exports;

namespace HVO.SkyMonitorV5.RPi.Options;

/// <summary>
/// Configuration for frame export sinks grouped by pipeline stage.
/// </summary>
public sealed class FrameExportOptions
{
    public const string SectionName = "FrameExport";

    public FrameExportStageOptions Raw { get; set; } = new();

    public FrameExportStageOptions Processed { get; set; } = new();

    public FrameExportStageOptions GetStageOptions(FrameExportStage stage) => stage switch
    {
        FrameExportStage.Raw => Raw ??= new FrameExportStageOptions(),
        FrameExportStage.Processed => Processed ??= new FrameExportStageOptions(),
        _ => Processed ??= new FrameExportStageOptions()
    };

    public void Normalize()
    {
        Raw ??= new FrameExportStageOptions();
        Raw.Normalize();

        Processed ??= new FrameExportStageOptions();
        Processed.Normalize();
    }
}

/// <summary>
/// Per-stage configuration for filesystem and object storage sinks.
/// </summary>
public sealed class FrameExportStageOptions
{
    public bool Enabled { get; set; }

    public IList<FilesystemFrameExportSinkOptions> Filesystem { get; } = new List<FilesystemFrameExportSinkOptions>();

    public IList<S3FrameExportSinkOptions> S3 { get; } = new List<S3FrameExportSinkOptions>();

    internal void Normalize()
    {
        foreach (var filesystem in Filesystem)
        {
            filesystem?.Normalize();
        }

        foreach (var s3 in S3)
        {
            s3?.Normalize();
        }
    }

    internal bool HasActiveFilesystemSink() => Enabled && Filesystem.Any(static option => option is { Enabled: true, RootPathLength: > 0 });

    internal bool HasActiveS3Sink() => Enabled && S3.Any(static option => option is { Enabled: true } && option.HasValidConfiguration);
}

/// <summary>
/// Filesystem sink configuration.
/// </summary>
public sealed class FilesystemFrameExportSinkOptions
{
    private string? _rootPath;
    private string? _prefix;

    public bool Enabled { get; set; } = true;

    [MaxLength(1024)]
    public string? RootPath
    {
        get => _rootPath;
        set => _rootPath = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public int RootPathLength => _rootPath?.Length ?? 0;

    [MaxLength(256)]
    public string? Prefix
    {
        get => _prefix;
    set => _prefix = string.IsNullOrWhiteSpace(value) ? null : value.Trim().Trim('/', '\\');
    }

    public bool IncludeMetadataManifest { get; set; } = true;

    public void Normalize()
    {
        RootPath = _rootPath;
        Prefix = _prefix;
    }

    internal IEnumerable<string> EnumeratePrefixSegments()
    {
        if (string.IsNullOrWhiteSpace(_prefix))
        {
            yield break;
        }

    var segments = _prefix.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            yield return segment;
        }
    }
}

/// <summary>
/// Object storage sink configuration for S3/MinIO endpoints.
/// </summary>
public sealed class S3FrameExportSinkOptions
{
    private string? _bucket;
    private string? _prefix;
    private string? _endpoint;
    private string? _accessKey;
    private string? _secretKey;
    private string? _region;

    public bool Enabled { get; set; } = true;

    [MaxLength(128)]
    public string? Bucket
    {
        get => _bucket;
        set => _bucket = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    [MaxLength(256)]
    public string? Prefix
    {
        get => _prefix;
        set => _prefix = string.IsNullOrWhiteSpace(value) ? null : value.Trim().Trim('/');
    }

    [MaxLength(256)]
    public string? Endpoint
    {
        get => _endpoint;
        set => _endpoint = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    [MaxLength(128)]
    public string? AccessKey
    {
        get => _accessKey;
        set => _accessKey = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    [MaxLength(128)]
    public string? SecretKey
    {
        get => _secretKey;
        set => _secretKey = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    [MaxLength(128)]
    public string? Region
    {
        get => _region;
        set => _region = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public bool UseSsl { get; set; } = true;

    public bool EmitMetadataHeaders { get; set; } = true;

    public bool EmitJsonManifest { get; set; } = true;

    internal bool HasCredentials => !string.IsNullOrWhiteSpace(_endpoint) && !string.IsNullOrWhiteSpace(_accessKey) && !string.IsNullOrWhiteSpace(_secretKey);

    internal bool HasValidConfiguration => HasCredentials && !string.IsNullOrWhiteSpace(_bucket);

    public void Normalize()
    {
        Bucket = _bucket;
        Prefix = _prefix;
        Endpoint = _endpoint;
        AccessKey = _accessKey;
        SecretKey = _secretKey;
        Region = _region;
    }

    internal string BuildObjectPrefix(FrameExportStage stage, DateTimeOffset timestamp)
    {
        var stageSegment = stage.ToString().ToLowerInvariant();

        var segments = new List<string>(6);

        if (!string.IsNullOrWhiteSpace(_prefix))
        {
            var prefixSegments = _prefix.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            segments.AddRange(prefixSegments);
        }

        if (segments.Count == 0 || !string.Equals(segments[^1], stageSegment, StringComparison.OrdinalIgnoreCase))
        {
            segments.Add(stageSegment);
        }

        segments.Add(timestamp.ToString("yyyy", CultureInfo.InvariantCulture));
        segments.Add(timestamp.ToString("MM", CultureInfo.InvariantCulture));
        segments.Add(timestamp.ToString("dd", CultureInfo.InvariantCulture));

        return string.Join('/', segments);
    }
}
