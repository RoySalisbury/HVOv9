#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace HVO.SkyMonitorV5.RPi.Services.RemoteDispatch;

public sealed class RemoteFramePublisher : IRemoteFramePublisher
{
    private readonly IOptionsMonitor<CameraPipelineOptions> _optionsMonitor;
    private readonly ILogger<RemoteFramePublisher> _logger;
    private readonly IMinioClientProvider _minioClientProvider;
    private readonly IRemoteFrameEncoder _frameEncoder;

    private static readonly Meter DispatchMeter = new("HVO.SkyMonitor.RemoteDispatch", "1.0.0");
    private static readonly Counter<long> DispatchSuccessCounter = DispatchMeter.CreateCounter<long>(
        name: "hvo.skymonitor.remote_dispatch.success",
        unit: "frames",
        description: "Number of frames successfully dispatched to remote destinations.");
    private static readonly Counter<long> DispatchFailureCounter = DispatchMeter.CreateCounter<long>(
        name: "hvo.skymonitor.remote_dispatch.failure",
        unit: "frames",
        description: "Number of frames that failed remote dispatch.");
    private static readonly Histogram<double> DispatchLatencyHistogram = DispatchMeter.CreateHistogram<double>(
        name: "hvo.skymonitor.remote_dispatch.latency_ms",
        unit: "ms",
        description: "Latency in milliseconds for remote dispatch operations.");
    private static readonly KeyValuePair<string, object?> ModeTagS3 = new("mode", RemoteDispatchMode.S3.ToString());

    public RemoteFramePublisher(
        IOptionsMonitor<CameraPipelineOptions> optionsMonitor,
        ILogger<RemoteFramePublisher> logger,
        IMinioClientProvider minioClientProvider,
        IRemoteFrameEncoder frameEncoder)
    {
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _minioClientProvider = minioClientProvider ?? throw new ArgumentNullException(nameof(minioClientProvider));
        _frameEncoder = frameEncoder ?? throw new ArgumentNullException(nameof(frameEncoder));
    }

    public Task<RemoteDispatchResult> PublishAsync(RemoteFrameEnvelope envelope, CancellationToken cancellationToken)
    {
        if (envelope is null)
        {
            throw new ArgumentNullException(nameof(envelope));
        }

        var remoteOptions = _optionsMonitor.CurrentValue.RemoteDispatch ?? new RemoteDispatchOptions();
        remoteOptions.Normalize();

        if (!remoteOptions.Enabled || remoteOptions.Mode == RemoteDispatchMode.None)
        {
            return Task.FromResult(RemoteDispatchResult.Disabled(RemoteDispatchMode.None.ToString(), "Remote dispatch disabled."));
        }

        return remoteOptions.Mode switch
        {
            RemoteDispatchMode.S3 => PublishToS3Async(envelope, remoteOptions, cancellationToken),
            _ => Task.FromResult(RemoteDispatchResult.Failure(remoteOptions.Mode.ToString(), "Unsupported remote dispatch mode."))
        };
    }

    private async Task<RemoteDispatchResult> PublishToS3Async(RemoteFrameEnvelope envelope, RemoteDispatchOptions dispatchOptions, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dispatchOptions.S3Bucket))
        {
            var result = RemoteDispatchResult.Failure(RemoteDispatchMode.S3.ToString(), "S3 bucket not configured.");
            _logger.LogWarning("Remote dispatch failed for frame {FrameNumber}: {Reason}", envelope.FrameNumber, result.Message);
            DispatchFailureCounter.Add(1, ModeTagS3);
            return result;
        }

        if (string.IsNullOrWhiteSpace(dispatchOptions.Endpoint))
        {
            const string reason = "Object storage endpoint not configured.";
            _logger.LogWarning("Remote dispatch failed for frame {FrameNumber}: {Reason}", envelope.FrameNumber, reason);
            DispatchFailureCounter.Add(1, ModeTagS3);
            return RemoteDispatchResult.Failure(RemoteDispatchMode.S3.ToString(), reason);
        }

        if (string.IsNullOrWhiteSpace(dispatchOptions.AccessKey) || string.IsNullOrWhiteSpace(dispatchOptions.SecretKey))
        {
            const string reason = "Object storage credentials not configured.";
            _logger.LogWarning("Remote dispatch failed for frame {FrameNumber}: {Reason}", envelope.FrameNumber, reason);
            DispatchFailureCounter.Add(1, ModeTagS3);
            return RemoteDispatchResult.Failure(RemoteDispatchMode.S3.ToString(), reason);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var image = envelope.CapturedFrame.Image;
        if (image is null)
        {
            const string reason = "Captured frame did not contain an image.";
            _logger.LogWarning("Remote dispatch failed for frame {FrameNumber}: {Reason}", envelope.FrameNumber, reason);
            DispatchFailureCounter.Add(1, ModeTagS3);
            return RemoteDispatchResult.Failure(RemoteDispatchMode.S3.ToString(), reason);
        }

        RemoteFramePayload payload;
        try
        {
            payload = _frameEncoder.Encode(envelope, dispatchOptions);
        }
        catch (Exception ex)
        {
            const string reason = "Failed to convert frame for remote dispatch.";
            _logger.LogWarning(ex, "Remote dispatch failed for frame {FrameNumber}: {Reason}", envelope.FrameNumber, reason);
            DispatchFailureCounter.Add(1, ModeTagS3);
            return RemoteDispatchResult.Failure(RemoteDispatchMode.S3.ToString(), reason, ex);
        }

        if (payload.Buffer.Length == 0)
        {
            const string reason = "Remote frame encoder returned an empty payload.";
            _logger.LogWarning("Remote dispatch failed for frame {FrameNumber}: {Reason}", envelope.FrameNumber, reason);
            DispatchFailureCounter.Add(1, ModeTagS3);
            return RemoteDispatchResult.Failure(RemoteDispatchMode.S3.ToString(), reason);
        }

        using var stream = new MemoryStream(payload.Buffer, writable: false);

        var key = BuildObjectKey(envelope, dispatchOptions, payload.FileExtension);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["rig-name"] = envelope.Rig.Name,
            ["frame-number"] = envelope.FrameNumber.ToString(CultureInfo.InvariantCulture),
            ["configuration-version"] = envelope.ConfigurationVersion.ToString(CultureInfo.InvariantCulture),
            ["background-stacker"] = envelope.UsingBackgroundStacker.ToString(),
            ["capture-milliseconds"] = envelope.CaptureMilliseconds.ToString(CultureInfo.InvariantCulture),
            ["captured-at-utc"] = envelope.CapturedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                ["payload-content-type"] = payload.ContentType,
                ["payload-file-extension"] = payload.FileExtension
        };

        var putArgs = new PutObjectArgs()
            .WithBucket(dispatchOptions.S3Bucket)
            .WithObject(key)
            .WithStreamData(stream)
            .WithObjectSize(stream.Length)
            .WithContentType(payload.ContentType)
            .WithHeaders(headers);

        var stopwatch = Stopwatch.StartNew();
        RemoteDispatchEventMetrics metrics;

        try
        {
            var client = _minioClientProvider.GetClient(dispatchOptions.Endpoint, dispatchOptions.AccessKey, dispatchOptions.SecretKey, dispatchOptions.UseSsl);
            await client.PutObjectAsync(putArgs, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            DispatchSuccessCounter.Add(1, ModeTagS3);
            DispatchLatencyHistogram.Record(stopwatch.Elapsed.TotalMilliseconds, ModeTagS3);

            metrics = new RemoteDispatchEventMetrics(
                LatencyMilliseconds: stopwatch.Elapsed.TotalMilliseconds,
                PayloadBytes: payload.Buffer.LongLength,
                PayloadContentType: payload.ContentType,
                PayloadFileExtension: payload.FileExtension);

            var scheme = dispatchOptions.UseSsl ? "https" : "http";
            var objectUrl = FormattableString.Invariant($"{scheme}://{dispatchOptions.Endpoint}/{dispatchOptions.S3Bucket}/{key}");
            _logger.LogInformation(
                "Remote dispatch uploaded frame {FrameNumber} to MinIO object {ObjectUrl} (fanout: {Fanout}).",
                envelope.FrameNumber,
                objectUrl,
                dispatchOptions.FanoutExchange ?? "default");

            var message = $"Uploaded remote frame to {objectUrl}.";
            return RemoteDispatchResult.Success(RemoteDispatchMode.S3.ToString(), message, metrics);
        }
        catch (MinioException ex) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            DispatchFailureCounter.Add(1, ModeTagS3);
            DispatchLatencyHistogram.Record(stopwatch.Elapsed.TotalMilliseconds, ModeTagS3);

            metrics = new RemoteDispatchEventMetrics(
                LatencyMilliseconds: stopwatch.Elapsed.TotalMilliseconds,
                PayloadBytes: payload.Buffer.LongLength,
                PayloadContentType: payload.ContentType,
                PayloadFileExtension: payload.FileExtension);

            _logger.LogWarning(ex, "MinIO rejected frame {FrameNumber} upload (bucket: {Bucket}, key: {Key}).", envelope.FrameNumber, dispatchOptions.S3Bucket, key);
            return RemoteDispatchResult.Failure(RemoteDispatchMode.S3.ToString(), "MinIO rejected the upload request.", ex, metrics);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            DispatchFailureCounter.Add(1, ModeTagS3);
            DispatchLatencyHistogram.Record(stopwatch.Elapsed.TotalMilliseconds, ModeTagS3);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            DispatchFailureCounter.Add(1, ModeTagS3);
            DispatchLatencyHistogram.Record(stopwatch.Elapsed.TotalMilliseconds, ModeTagS3);
            _logger.LogError(ex, "Remote dispatch failed for frame {FrameNumber}.", envelope.FrameNumber);
            metrics = new RemoteDispatchEventMetrics(
                LatencyMilliseconds: stopwatch.Elapsed.TotalMilliseconds,
                PayloadBytes: payload.Buffer.LongLength,
                PayloadContentType: payload.ContentType,
                PayloadFileExtension: payload.FileExtension);

            return RemoteDispatchResult.Failure(RemoteDispatchMode.S3.ToString(), "Remote dispatch encountered an unexpected error.", ex, metrics);
        }
    }

    private static string BuildObjectKey(RemoteFrameEnvelope envelope, RemoteDispatchOptions options, string fileExtension)
    {
        var fanoutSegment = SanitizeSegment(options.FanoutExchange) ?? "default";
        var rigSegment = SanitizeSegment(envelope.Rig.Name) ?? "rig";
        var timestamp = envelope.CapturedAtUtc;

        var dateSegment = timestamp.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
        var timeSegment = timestamp.ToString("HHmmssfff", CultureInfo.InvariantCulture);

        var extension = string.IsNullOrWhiteSpace(fileExtension) ? "bin" : SanitizeSegment(fileExtension) ?? "bin";
        if (!extension.Contains('.', StringComparison.Ordinal))
        {
            extension = $".{extension}";
        }

        return FormattableString.Invariant($"{fanoutSegment}/{rigSegment}/{dateSegment}/frame-{envelope.FrameNumber:D6}-{timeSegment}{extension}");
    }

    private static string? SanitizeSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        Span<char> buffer = stackalloc char[value.Length];
        var length = 0;

        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                buffer[length++] = char.ToLowerInvariant(ch);
                continue;
            }

            if (ch is '-' or '_' or '.')
            {
                buffer[length++] = ch;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                buffer[length++] = '-';
            }
        }

        return length == 0 ? null : new string(buffer[..length]);
    }
}
