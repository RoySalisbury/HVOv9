using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Services.RemoteDispatch;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio.DataModel.Args;

namespace HVO.SkyMonitorV5.RPi.Infrastructure.HealthChecks;

/// <summary>
/// ASP.NET Core health check that verifies connectivity to all enabled S3/MinIO sinks
/// configured for frame export (raw and processed) and confirms bucket existence.
/// </summary>
public sealed class S3FrameExportHealthCheck : IHealthCheck
{
  private readonly IOptionsMonitor<FrameExportOptions> _options;
  private readonly IMinioClientProvider _minio;
  private readonly ILogger<S3FrameExportHealthCheck>? _logger;

  public S3FrameExportHealthCheck(
      IOptionsMonitor<FrameExportOptions> options,
      IMinioClientProvider minio,
      ILogger<S3FrameExportHealthCheck>? logger = null)
  {
    _options = options ?? throw new ArgumentNullException(nameof(options));
    _minio = minio ?? throw new ArgumentNullException(nameof(minio));
    _logger = logger;
  }

  public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
  {
    var cfg = _options.CurrentValue;

    var enabledSinks = EnumerateEnabledSinks(cfg).ToList();
    if (enabledSinks.Count == 0)
    {
      // Nothing to check; treat as healthy so liveness and readiness remain green without S3 usage
      return HealthCheckResult.Healthy("No S3 sinks enabled.");
    }

    var failures = new List<(string Stage, string Endpoint, string Bucket, string Reason)>();

    foreach (var sink in enabledSinks)
    {
      try
      {
        var client = _minio.GetClient(sink.Endpoint!, sink.AccessKey!, sink.SecretKey!, sink.UseSsl ?? false);
        var exists = await client.BucketExistsAsync(new BucketExistsArgs().WithBucket(sink.Bucket!), cancellationToken).ConfigureAwait(false);
        if (!exists)
        {
          failures.Add((sink.Stage, sink.Endpoint!, sink.Bucket!, "Bucket does not exist or is not accessible."));
        }
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        throw;
      }
      catch (Exception ex)
      {
        _logger?.LogWarning(ex, "S3 health check failed for stage {Stage}, bucket {Bucket}, endpoint {Endpoint}.", sink.Stage, sink.Bucket, sink.Endpoint);
        failures.Add((sink.Stage, sink.Endpoint ?? "(null)", sink.Bucket ?? "(null)", ex.Message));
      }
    }

    if (failures.Count == 0)
    {
      return HealthCheckResult.Healthy("All enabled S3 sinks reachable and buckets verified.");
    }

    var status = failures.Count == enabledSinks.Count ? HealthStatus.Unhealthy : HealthStatus.Degraded;

    var data = new Dictionary<string, object>
    {
      ["enabledSinkCount"] = enabledSinks.Count,
      ["failureCount"] = failures.Count,
      ["failures"] = failures.Select(f => new { f.Stage, f.Endpoint, f.Bucket, f.Reason }).ToArray()
    };

    return new HealthCheckResult(status, "One or more S3 sinks failed checks.", data: data);
  }

  private static IEnumerable<(string Stage, string? Endpoint, string? Bucket, string? AccessKey, string? SecretKey, bool? UseSsl)> EnumerateEnabledSinks(FrameExportOptions cfg)
  {
    // Raw stage
    if (cfg.Raw?.Enabled == true && cfg.Raw.S3 is not null)
    {
      foreach (var s in cfg.Raw.S3.Where(s => s.Enabled))
      {
        if (IsValid(s))
        {
          yield return ("Raw", s.Endpoint, s.Bucket, s.AccessKey, s.SecretKey, s.UseSsl);
        }
      }
    }

    // Processed stage
    if (cfg.Processed?.Enabled == true && cfg.Processed.S3 is not null)
    {
      foreach (var s in cfg.Processed.S3.Where(s => s.Enabled))
      {
        if (IsValid(s))
        {
          yield return ("Processed", s.Endpoint, s.Bucket, s.AccessKey, s.SecretKey, s.UseSsl);
        }
      }
    }

    static bool IsValid(S3FrameExportSinkOptions s) =>
        !string.IsNullOrWhiteSpace(s.Endpoint)
        && !string.IsNullOrWhiteSpace(s.Bucket)
        && !string.IsNullOrWhiteSpace(s.AccessKey)
        && !string.IsNullOrWhiteSpace(s.SecretKey);
  }
}
