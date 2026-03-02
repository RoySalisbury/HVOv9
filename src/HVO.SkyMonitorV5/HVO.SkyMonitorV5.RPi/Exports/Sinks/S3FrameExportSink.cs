using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HVO.Core.Results;
using HVO.SkyMonitorV5.RPi.Infrastructure.Resilience;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Services.RemoteDispatch;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using System.Linq;
using Polly;

namespace HVO.SkyMonitorV5.RPi.Exports.Sinks;

/// <summary>
/// Persists frame export payloads to S3-compatible object storage using MinIO.
/// </summary>
public sealed class S3FrameExportSink : IFrameExportSink
{
    private static readonly JsonSerializerOptions ManifestSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly FrameExportStage _stage;
    private readonly IOptionsMonitor<FrameExportOptions> _optionsMonitor;
    private readonly IMinioClientProvider _clientProvider;
    private readonly IFrameExportResiliencePolicyProvider _resiliencePolicyProvider;
    private readonly HealthCheckService _healthChecks;
    private readonly ILogger<S3FrameExportSink> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _bucketLocks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _initializedBuckets = new(StringComparer.Ordinal);
    private readonly object _healthGateLock = new();
    private DateTimeOffset _lastHealthCheckUtc = DateTimeOffset.MinValue;
    private HealthStatus _lastHealthStatus = HealthStatus.Healthy;
    private static readonly TimeSpan HealthCheckThrottle = TimeSpan.FromSeconds(5);

    public S3FrameExportSink(
        FrameExportStage stage,
        IOptionsMonitor<FrameExportOptions> optionsMonitor,
        IMinioClientProvider clientProvider,
        IFrameExportResiliencePolicyProvider resiliencePolicyProvider,
        HealthCheckService healthChecks,
        ILogger<S3FrameExportSink> logger)
    {
        _stage = stage;
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _clientProvider = clientProvider ?? throw new ArgumentNullException(nameof(clientProvider));
        _resiliencePolicyProvider = resiliencePolicyProvider ?? throw new ArgumentNullException(nameof(resiliencePolicyProvider));
        _healthChecks = healthChecks ?? throw new ArgumentNullException(nameof(healthChecks));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Name => "s3";

    public bool SupportsStage(FrameExportStage stage)
    {
        if (stage != _stage)
        {
            return false;
        }

        var options = StageOptions;
        return options.Enabled && options.HasActiveS3Sink();
    }

    public async ValueTask<Result<bool>> ExportAsync(FrameExportEnvelope envelope, CancellationToken cancellationToken)
    {
        if (envelope is null)
        {
            throw new ArgumentNullException(nameof(envelope));
        }

        // Gate on readiness: if the S3 health check status isn't Healthy, skip exporting to S3.
        // Throttle health checks to avoid excessive MinIO calls.
        var nowUtc = DateTimeOffset.UtcNow;
        var status = _lastHealthStatus;
        var needsCheck = false;
        lock (_healthGateLock)
        {
            if (nowUtc - _lastHealthCheckUtc >= HealthCheckThrottle)
            {
                needsCheck = true;
            }
        }

        if (needsCheck)
        {
            try
            {
                var report = await _healthChecks.CheckHealthAsync(r => string.Equals(r.Name, "s3_export", StringComparison.Ordinal), cancellationToken).ConfigureAwait(false);
                status = report.Status;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // If the health check fails unexpectedly, treat as Unhealthy to be safe.
                status = HealthStatus.Unhealthy;
                _logger.LogWarning(ex, "S3 readiness probe failed; exports will be skipped until healthy.");
            }

            lock (_healthGateLock)
            {
                _lastHealthStatus = status;
                _lastHealthCheckUtc = nowUtc;
            }
        }

        if (status != HealthStatus.Healthy)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Skipping S3 export for frame {FrameId} ({Stage}) due to readiness status {Status}.", envelope.FrameId, envelope.Stage, status);
            }
            return Result<bool>.Success(false);
        }

        var options = StageOptions;
        if (!options.Enabled)
        {
            return Result<bool>.Success(false);
        }

        var configurations = options.S3
            .Where(static option => option is { Enabled: true } && option.HasValidConfiguration)
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
                    await UploadAsync(configuration, envelope, role, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (firstError is null)
                {
                    firstError = ex;
                    _logger.LogError(ex,
                        "S3 export sink failed for frame {FrameId} ({Stage}) [{Role}] targeting bucket {Bucket}.",
                        envelope.FrameId,
                        envelope.Stage,
                        role,
                        configuration.Bucket);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "S3 export sink encountered an additional failure for frame {FrameId} ({Stage}) [{Role}] targeting bucket {Bucket}.",
                        envelope.FrameId,
                        envelope.Stage,
                        role,
                        configuration.Bucket);
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

    private async Task UploadAsync(
        S3FrameExportSinkOptions configuration,
        FrameExportEnvelope envelope,
        FrameExportPayloadRole role,
        CancellationToken cancellationToken)
    {
        var bucket = configuration.Bucket;
        if (string.IsNullOrWhiteSpace(bucket))
        {
            throw new InvalidOperationException("S3 bucket must be configured for frame export.");
        }

        var endpoint = configuration.Endpoint;
        var accessKey = configuration.AccessKey;
        var secretKey = configuration.SecretKey;
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretKey))
        {
            throw new InvalidOperationException("S3 credentials and endpoint must be configured for frame export.");
        }

        var client = _clientProvider.GetClient(endpoint, accessKey, secretKey, configuration.UseSsl);

        await EnsureBucketExistsAsync(client, bucket, configuration, cancellationToken).ConfigureAwait(false);

        var timestampUtc = FrameExportPathUtilities.ResolveStageTimestamp(envelope.Metadata);

        var prefix = configuration.BuildObjectPrefix(role, timestampUtc);
        var baseName = FrameExportPathUtilities.BuildBaseFileName(timestampUtc, envelope.FrameId);
        var metadataExtension = envelope.Metadata.PayloadExtension;
        var extension = FrameExportPathUtilities.ResolveExtension(string.IsNullOrWhiteSpace(metadataExtension) ? envelope.FileExtension : metadataExtension);
        var objectKey = FormattableString.Invariant($"{prefix}/{baseName}.{extension}");
        var metadataContentType = envelope.Metadata.PayloadContentType;
        var contentType = string.IsNullOrWhiteSpace(metadataContentType)
            ? string.IsNullOrWhiteSpace(envelope.ContentType) ? "application/octet-stream" : envelope.ContentType
            : metadataContentType;

        await UploadObjectAsync(client, bucket, objectKey, contentType, configuration, envelope, role, cancellationToken).ConfigureAwait(false);

        if (configuration.EmitJsonManifest)
        {
            var manifestKey = FormattableString.Invariant($"{prefix}/{baseName}.json");
            await UploadManifestAsync(client, bucket, manifestKey, envelope.Metadata, cancellationToken).ConfigureAwait(false);
        }

        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogTrace(
                "Uploaded frame export {FrameId} ({Stage}) [{Role}] to s3://{Bucket}/{Key}.",
                envelope.FrameId,
                envelope.Stage,
                role,
                bucket,
                objectKey);
        }
    }

    private async Task UploadObjectAsync(
        IMinioClient client,
        string bucket,
        string objectKey,
        string contentType,
        S3FrameExportSinkOptions configuration,
        FrameExportEnvelope envelope,
        FrameExportPayloadRole role,
        CancellationToken cancellationToken)
    {
        await using var payloadStream = new MemoryStream(envelope.Payload.ToArray(), writable: false);

        var putArgs = new PutObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectKey)
            .WithStreamData(payloadStream)
            .WithObjectSize(payloadStream.Length)
            .WithContentType(contentType);

        if (configuration.EmitMetadataHeaders)
        {
            var headers = BuildMetadataHeaders(envelope);
            headers["payload-role"] = role.ToString().ToLowerInvariant();
            if (headers.Count > 0)
            {
                putArgs = putArgs.WithHeaders(headers);
            }
        }

        await ExecuteWithResilienceAsync(async token =>
        {
            try
            {
                await client.PutObjectAsync(putArgs, token).ConfigureAwait(false);
            }
            catch (MinioException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(FormattableString.Invariant($"Failed to upload frame export to s3://{bucket}/{objectKey}"), ex);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureBucketExistsAsync(IMinioClient client, string bucket, S3FrameExportSinkOptions configuration, CancellationToken cancellationToken)
    {
        var cacheKey = BuildBucketCacheKey(configuration, bucket);
        if (_initializedBuckets.ContainsKey(cacheKey))
        {
            return;
        }

        var gate = _bucketLocks.GetOrAdd(cacheKey, static _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_initializedBuckets.ContainsKey(cacheKey))
            {
                return;
            }

            var created = false;

            await ExecuteWithResilienceAsync(async token =>
            {
                var bucketExistsArgs = new BucketExistsArgs()
                    .WithBucket(bucket);

                var exists = await client.BucketExistsAsync(bucketExistsArgs, token).ConfigureAwait(false);
                if (exists)
                {
                    return;
                }

                var makeBucketArgs = new MakeBucketArgs()
                    .WithBucket(bucket);

                try
                {
                    await client.MakeBucketAsync(makeBucketArgs, token).ConfigureAwait(false);
                    created = true;
                }
                catch (MinioException)
                {
                    var postCheckExists = await client.BucketExistsAsync(bucketExistsArgs, token).ConfigureAwait(false);
                    if (!postCheckExists)
                    {
                        throw;
                    }
                }
            }, cancellationToken).ConfigureAwait(false);

            if (created)
            {
                _logger.LogInformation(
                    "Created S3 bucket {Bucket} for frame export stage {Stage} targeting endpoint {Endpoint} (SSL: {UseSsl}).",
                    bucket,
                    _stage,
                    configuration.Endpoint,
                    configuration.UseSsl);
            }

            _initializedBuckets[cacheKey] = true;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task UploadManifestAsync(IMinioClient client, string bucket, string objectKey, FrameExportMetadata metadata, CancellationToken cancellationToken)
    {
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(metadata, ManifestSerializerOptions);
        await using var manifestStream = new MemoryStream(manifestBytes, writable: false);

        var manifestArgs = new PutObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectKey)
            .WithStreamData(manifestStream)
            .WithObjectSize(manifestStream.Length)
            .WithContentType("application/json");

        await ExecuteWithResilienceAsync(async token =>
        {
            await client.PutObjectAsync(manifestArgs, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static Dictionary<string, string> BuildMetadataHeaders(FrameExportEnvelope envelope)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["frame-id"] = envelope.FrameId.ToString("D", CultureInfo.InvariantCulture),
            ["frame-stage"] = envelope.Stage.ToString().ToLowerInvariant(),
            ["captured-at-utc"] = envelope.Metadata.CapturedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            ["stage-timestamp-utc"] = envelope.Metadata.StageTimestampUtc.ToString("O", CultureInfo.InvariantCulture),
            ["rig-name"] = SanitizeMetadataValue(envelope.Metadata.RigName),
            ["camera-name"] = SanitizeMetadataValue(envelope.Metadata.CameraName),
            ["lens-name"] = SanitizeMetadataValue(envelope.Metadata.LensName),
            ["latitude-deg"] = envelope.Metadata.LatitudeDeg.ToString(CultureInfo.InvariantCulture),
            ["longitude-deg"] = envelope.Metadata.LongitudeDeg.ToString(CultureInfo.InvariantCulture),
            ["flip-horizontal"] = envelope.Metadata.FlipHorizontal.ToString(CultureInfo.InvariantCulture),
            ["apply-refraction"] = envelope.Metadata.ApplyRefraction.ToString(CultureInfo.InvariantCulture)
        };

        if (envelope.Metadata.Exposure is { } exposure)
        {
            headers["exposure-ms"] = exposure.ExposureMilliseconds.ToString(CultureInfo.InvariantCulture);
            headers["gain"] = exposure.Gain.ToString(CultureInfo.InvariantCulture);
        }

        if (envelope.Metadata.FramesStacked is int stacked)
        {
            headers["frames-stacked"] = stacked.ToString(CultureInfo.InvariantCulture);
        }

        if (envelope.Metadata.IntegrationMilliseconds is int integration)
        {
            headers["integration-ms"] = integration.ToString(CultureInfo.InvariantCulture);
        }

        if (envelope.Metadata.QueueLatencyMilliseconds is double queueLatency)
        {
            headers["queue-latency-ms"] = queueLatency.ToString("F3", CultureInfo.InvariantCulture);
        }

        if (envelope.Metadata.ProcessingMilliseconds is double processingLatency)
        {
            headers["processing-ms"] = processingLatency.ToString("F3", CultureInfo.InvariantCulture);
        }

        if (envelope.Metadata.FullPipelineMilliseconds is double fullPipeline)
        {
            headers["full-pipeline-ms"] = fullPipeline.ToString("F3", CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(envelope.Metadata.PayloadContentType))
        {
            headers["payload-content-type"] = SanitizeMetadataValue(envelope.Metadata.PayloadContentType);
        }

        if (!string.IsNullOrWhiteSpace(envelope.Metadata.PayloadExtension))
        {
            headers["payload-extension"] = SanitizeMetadataValue(envelope.Metadata.PayloadExtension);
        }

        if (envelope.Metadata.AppliedFilters is { Count: > 0 } filters)
        {
            headers["applied-filters"] = SanitizeMetadataValue(string.Join(';', filters));
        }

        if (envelope.Metadata.RawImageDescriptor is { } descriptor)
        {
            headers["raw-width"] = descriptor.Width.ToString(CultureInfo.InvariantCulture);
            headers["raw-height"] = descriptor.Height.ToString(CultureInfo.InvariantCulture);
            headers["raw-rowbytes"] = descriptor.RowBytes.ToString(CultureInfo.InvariantCulture);
            headers["raw-bytes-per-pixel"] = descriptor.BytesPerPixel.ToString(CultureInfo.InvariantCulture);
            headers["raw-color-type"] = SanitizeMetadataValue(descriptor.ColorType);
            headers["raw-alpha-type"] = SanitizeMetadataValue(descriptor.AlphaType);
            headers["raw-gamma-linear"] = descriptor.GammaIsLinear.ToString(CultureInfo.InvariantCulture);
            headers["raw-is-srgb"] = descriptor.IsSrgb.ToString(CultureInfo.InvariantCulture);
            headers["raw-transfer-numeric"] = descriptor.HasNumericalTransferFunction.ToString(CultureInfo.InvariantCulture);

            if (!string.IsNullOrWhiteSpace(descriptor.ColorSpaceDescription))
            {
                headers["raw-color-space"] = SanitizeMetadataValue(descriptor.ColorSpaceDescription);
            }
        }

        return headers;
    }

    private static string SanitizeMetadataValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        Span<char> buffer = stackalloc char[Math.Min(value.Length, 256)];
        var length = 0;
        foreach (var ch in value)
        {
            if (length >= buffer.Length)
            {
                break;
            }

            if (ch < 32 || ch > 126)
            {
                buffer[length++] = '_';
                continue;
            }

            buffer[length++] = ch;
        }

        return length == 0 ? string.Empty : new string(buffer[..length]);
    }

    private async Task ExecuteWithResilienceAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var policy = _resiliencePolicyProvider.CreatePolicy();

        if (policy is null)
        {
            await operation(cancellationToken).ConfigureAwait(false);
            return;
        }

        await policy.ExecuteAsync(async token =>
        {
            await operation(token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildBucketCacheKey(S3FrameExportSinkOptions configuration, string bucket)
    {
        var endpoint = configuration.Endpoint?.Trim() ?? string.Empty;
        endpoint = endpoint.TrimEnd('/');
        var useSslToken = configuration.UseSsl ? "1" : "0";

        return FormattableString.Invariant($"{endpoint}|{useSslToken}|{bucket}");
    }
}
