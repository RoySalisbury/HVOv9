using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.RPi.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.SkyMonitorV5.RPi.Exports.Sinks;

/// <summary>
/// Persists frame export payloads to a configurable filesystem hierarchy.
/// </summary>
public sealed class FilesystemFrameExportSink : IFrameExportSink
{
    private static readonly JsonSerializerOptions ManifestSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly FrameExportStage _stage;
    private readonly IOptionsMonitor<FrameExportOptions> _optionsMonitor;
    private readonly ILogger<FilesystemFrameExportSink> _logger;

    public FilesystemFrameExportSink(
        FrameExportStage stage,
        IOptionsMonitor<FrameExportOptions> optionsMonitor,
        ILogger<FilesystemFrameExportSink> logger)
    {
        _stage = stage;
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Name => "filesystem";

    public bool SupportsStage(FrameExportStage stage)
    {
        if (stage != _stage)
        {
            return false;
        }

        var options = StageOptions;
        return options.Enabled && options.HasActiveFilesystemSink();
    }

    public async ValueTask<Result<bool>> ExportAsync(FrameExportEnvelope envelope, CancellationToken cancellationToken)
    {
        if (envelope is null)
        {
            throw new ArgumentNullException(nameof(envelope));
        }

        var options = StageOptions;
        if (!options.Enabled)
        {
            return Result<bool>.Success(false);
        }

        var configurations = options.Filesystem
            .Where(static option => option is { Enabled: true, RootPathLength: > 0 })
            .ToArray();

        var roles = options.EnumerateRoles().ToArray();
        if (roles.Length == 0)
        {
            return Result<bool>.Success(false);
        }

        if (configurations.Length == 0)
        {
            return Result<bool>.Success(false);
        }

        Exception? firstError = null;
        foreach (var configuration in configurations)
        {
            foreach (var role in roles)
            {
                try
                {
                    await PersistAsync(configuration, envelope, role, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (firstError is null)
                {
                    firstError = ex;
                    _logger.LogError(ex,
                        "Filesystem export sink failed for frame {FrameId} ({Stage}) [{Role}] at root {Root}.",
                        envelope.FrameId,
                        envelope.Stage,
                        role,
                        configuration.RootPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Filesystem export sink encountered an additional failure for frame {FrameId} ({Stage}) [{Role}] at root {Root}.",
                        envelope.FrameId,
                        envelope.Stage,
                        role,
                        configuration.RootPath);
                }
            }
        }

        if (firstError is not null)
        {
            return Result<bool>.Failure(firstError);
        }

        return Result<bool>.Success(true);
    }

    private FrameExportStageOptions StageOptions => _optionsMonitor.CurrentValue.GetStageOptions(_stage);

    private async Task PersistAsync(
        FilesystemFrameExportSinkOptions configuration,
        FrameExportEnvelope envelope,
        FrameExportPayloadRole role,
        CancellationToken cancellationToken)
    {
        var timestampUtc = envelope.Metadata.StageTimestampUtc;
        if (timestampUtc == default)
        {
            timestampUtc = DateTimeOffset.UtcNow;
        }

        var stageDirectory = GetRoleDirectoryName(role);
        var pathSegments = BuildPathSegments(configuration, stageDirectory, timestampUtc);
        var directory = Path.Combine(pathSegments);
        Directory.CreateDirectory(directory);

        var baseName = BuildBaseFileName(timestampUtc, envelope.FrameId);
        var extension = ResolveExtension(envelope.FileExtension);
        var payloadPath = Path.Combine(directory, FormattableString.Invariant($"{baseName}.{extension}"));
        await WriteFileAtomicAsync(payloadPath, envelope.Payload, cancellationToken).ConfigureAwait(false);

        if (configuration.IncludeMetadataManifest)
        {
            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(envelope.Metadata, ManifestSerializerOptions);
            var manifestPath = Path.Combine(directory, FormattableString.Invariant($"{baseName}.json"));
            await WriteFileAtomicAsync(manifestPath, manifestBytes, cancellationToken).ConfigureAwait(false);
        }

        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogTrace(
                "Persisted frame export {FrameId} ({Stage}) to {Path}.",
                envelope.FrameId,
                envelope.Stage,
                payloadPath);
        }
    }

    private static string[] BuildPathSegments(FilesystemFrameExportSinkOptions configuration, string scopeDirectory, DateTimeOffset timestampUtc)
    {
        var segments = new List<string>(8)
        {
            configuration.RootPath!
        };

        var prefixSegments = configuration.EnumeratePrefixSegments().ToArray();
        segments.AddRange(prefixSegments);

        if (prefixSegments.Length == 0 || !string.Equals(prefixSegments[^1], scopeDirectory, StringComparison.OrdinalIgnoreCase))
        {
            segments.Add(scopeDirectory);
        }
        segments.Add(timestampUtc.ToString("yyyy", CultureInfo.InvariantCulture));
        segments.Add(timestampUtc.ToString("MM", CultureInfo.InvariantCulture));
        segments.Add(timestampUtc.ToString("dd", CultureInfo.InvariantCulture));

        return segments.ToArray();
    }

    private static string BuildBaseFileName(DateTimeOffset timestampUtc, Guid frameId)
        => FormattableString.Invariant($"{timestampUtc:HHmmssfff}-{frameId:N}");

    private static string ResolveExtension(string? extension)
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

    private static async Task WriteFileAtomicAsync(string path, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = Path.Combine(directory ?? Path.GetTempPath(), FormattableString.Invariant($".tmp-{Guid.NewGuid():N}"));
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous))
            {
                await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }
    }

    private static string GetRoleDirectoryName(FrameExportPayloadRole role) => role switch
    {
        FrameExportPayloadRole.Archive => "archive",
        FrameExportPayloadRole.Delivery => "delivery",
        _ => "unknown"
    };
}
