using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.Data.Telemetry;
using HVO.SkyMonitorV5.Data.Telemetry.Entities;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.SkyMonitorV5.RPi.Exports;

/// <summary>
/// Persists failed export envelopes and replays them with backoff until success or abandonment.
/// </summary>
public sealed class FrameExportRetryService : BackgroundService, IFrameExportRetryQueue
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private const string UnknownFailureMessage = "Sink reported unsuccessful result.";

    private readonly Channel<FrameExportRetryRequest> _scheduleChannel;
    private readonly IDbContextFactory<SkyMonitorTelemetryContext> _telemetryContextFactory;
    private readonly IReadOnlyDictionary<string, IFrameExportSink> _sinkLookup;
    private readonly IOptionsMonitor<FrameExportRetryOptions> _optionsMonitor;
    private readonly IObservatoryClock _clock;
    private readonly FrameExportMetrics _metrics;
    private readonly ILogger<FrameExportRetryService> _logger;

    public FrameExportRetryService(
        IEnumerable<IFrameExportSink> sinks,
        IDbContextFactory<SkyMonitorTelemetryContext> telemetryContextFactory,
        IOptionsMonitor<FrameExportRetryOptions> optionsMonitor,
        IObservatoryClock clock,
        FrameExportMetrics metrics,
        ILogger<FrameExportRetryService> logger)
    {
        _scheduleChannel = Channel.CreateUnbounded<FrameExportRetryRequest>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        _telemetryContextFactory = telemetryContextFactory ?? throw new ArgumentNullException(nameof(telemetryContextFactory));
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var sinkMap = new Dictionary<string, IFrameExportSink>(StringComparer.OrdinalIgnoreCase);
        foreach (var sink in sinks ?? throw new ArgumentNullException(nameof(sinks)))
        {
            sinkMap[sink.Name] = sink;
        }

        _sinkLookup = sinkMap;
    }

    public ValueTask ScheduleRetryAsync(FrameExportRetryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Envelope);

        var options = _optionsMonitor.CurrentValue;
        if (!options.Enabled)
        {
            _logger.LogTrace("Frame export retry queue disabled; dropping payload {FrameId} for sink {Sink}.", request.Envelope.FrameId, request.SinkName);
            return ValueTask.CompletedTask;
        }

        if (!_sinkLookup.ContainsKey(request.SinkName))
        {
            _logger.LogWarning("Retry requested for frame {FrameId} but sink {Sink} is not registered.", request.Envelope.FrameId, request.SinkName);
            return ValueTask.CompletedTask;
        }

        return _scheduleChannel.Writer.WriteAsync(request, cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = _scheduleChannel.Reader;
        var currentInterval = NormalizeOptions(_optionsMonitor.CurrentValue).PollInterval;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (reader.TryRead(out var request))
                {
                    await HandleScheduleAsync(request, stoppingToken).ConfigureAwait(false);
                }

                await ProcessDueItemsAsync(stoppingToken).ConfigureAwait(false);

                var waitForReadTask = CancellationTokenHelpers.WaitToReadWithoutThrowAsync(reader, stoppingToken).AsTask();
                var delayTask = CancellationTokenHelpers.DelayWithoutThrowAsync(currentInterval, stoppingToken);
                var completed = await Task.WhenAny(waitForReadTask, delayTask).ConfigureAwait(false);

                if (completed == waitForReadTask)
                {
                    if (!await waitForReadTask.ConfigureAwait(false))
                    {
                        break;
                    }
                }
                else
                {
                    var delayCompleted = await delayTask.ConfigureAwait(false);
                    if (!delayCompleted)
                    {
                        break;
                    }

                    var latest = NormalizeOptions(_optionsMonitor.CurrentValue).PollInterval;
                    if (latest != currentInterval)
                    {
                        currentInterval = latest;
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in frame export retry loop.");
                var delayCompleted = await CancellationTokenHelpers.DelayWithoutThrowAsync(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
                if (!delayCompleted)
                {
                    break;
                }
            }
        }
    }

    private async Task HandleScheduleAsync(FrameExportRetryRequest request, CancellationToken cancellationToken)
    {
        var options = NormalizeOptions(_optionsMonitor.CurrentValue);
        if (!options.Enabled)
        {
            return;
        }

        var envelope = request.Envelope;
        if (!_sinkLookup.TryGetValue(request.SinkName, out var sink))
        {
            _logger.LogWarning("Retry requested for frame {FrameId} but sink {Sink} is unavailable.", envelope.FrameId, request.SinkName);
            return;
        }

        await using var context = await _telemetryContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        if (options.MaxQueueSize > 0)
        {
            var currentCount = await context.FrameExportRetries.LongCountAsync(cancellationToken).ConfigureAwait(false);
            if (currentCount >= options.MaxQueueSize)
            {
                _logger.LogWarning(
                    "Frame export retry queue is full ({Count}); dropping payload {FrameId} for sink {Sink}.",
                    currentCount,
                    envelope.FrameId,
                    request.SinkName);
                return;
            }
        }

        var nowUtc = _clock.UtcNow;
        var attemptCount = Math.Max(1, request.AttemptCount);
        var entity = new FrameExportRetryEntity
        {
            FrameId = envelope.FrameId,
            Stage = (int)envelope.Stage,
            SinkName = sink.Name,
            EnqueuedAtUtc = nowUtc,
            AttemptCount = attemptCount,
            NextAttemptAtUtc = CalculateNextAttempt(nowUtc, options, attemptCount),
            Payload = envelope.Payload.ToArray(),
            ContentType = envelope.ContentType,
            FileExtension = envelope.FileExtension,
            MetadataJson = JsonSerializer.Serialize(envelope.Metadata, SerializerOptions),
            LastErrorMessage = request.ErrorMessage
        };

        context.FrameExportRetries.Add(entity);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await UpdatePendingCountAsync(context, cancellationToken).ConfigureAwait(false);

        _logger.LogDebug(
            "Scheduled frame export retry for frame {FrameId} via sink {Sink} in {Delay} (attempt {Attempt}).",
            envelope.FrameId,
            sink.Name,
            entity.NextAttemptAtUtc - nowUtc,
            attemptCount);
    }

    private async Task ProcessDueItemsAsync(CancellationToken stoppingToken)
    {
        var options = NormalizeOptions(_optionsMonitor.CurrentValue);
        if (!options.Enabled)
        {
            return;
        }

        await using var context = await _telemetryContextFactory.CreateDbContextAsync(stoppingToken).ConfigureAwait(false);
        var nowUtc = _clock.UtcNow;

        var pendingItems = await context.FrameExportRetries
            .AsTracking()
            .ToListAsync(stoppingToken)
            .ConfigureAwait(false);

        if (pendingItems.Count == 0)
        {
            return;
        }

        var dueItems = pendingItems
            .Where(entity => entity.NextAttemptAtUtc <= nowUtc)
            .OrderBy(entity => entity.NextAttemptAtUtc)
            .Take(options.BatchSize)
            .ToList();

        if (dueItems.Count == 0)
        {
            return;
        }

        foreach (var entity in dueItems)
        {
            stoppingToken.ThrowIfCancellationRequested();

            if (!_sinkLookup.TryGetValue(entity.SinkName, out var sink))
            {
                _logger.LogWarning(
                    "Dropping frame export retry for frame {FrameId}; sink {Sink} is no longer registered.",
                    entity.FrameId,
                    entity.SinkName);
                context.FrameExportRetries.Remove(entity);
                continue;
            }

            var stage = (FrameExportStage)entity.Stage;
            if (!sink.SupportsStage(stage))
            {
                _logger.LogWarning(
                    "Dropping frame export retry for frame {FrameId}; sink {Sink} no longer supports stage {Stage}.",
                    entity.FrameId,
                    entity.SinkName,
                    stage);
                context.FrameExportRetries.Remove(entity);
                continue;
            }

            if (!TryDeserializeMetadata(entity.MetadataJson, out var metadata))
            {
                _logger.LogWarning(
                    "Dropping frame export retry for frame {FrameId}; metadata could not be deserialized.",
                    entity.FrameId);
                context.FrameExportRetries.Remove(entity);
                continue;
            }

            var envelope = new FrameExportEnvelope(
                entity.FrameId,
                stage,
                metadata!,
                new ReadOnlyMemory<byte>(entity.Payload),
                string.IsNullOrWhiteSpace(entity.ContentType) ? "application/octet-stream" : entity.ContentType!,
                entity.FileExtension);

            Result<bool> result;
            try
            {
                result = await sink.ExportAsync(envelope, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                result = Result<bool>.Failure(ex);
            }

            var success = result.IsSuccessful && result.Value;
            if (success)
            {
                context.FrameExportRetries.Remove(entity);
                _logger.LogInformation(
                    "Frame export retry succeeded for frame {FrameId} via sink {Sink} after {Attempts} attempt(s).",
                    entity.FrameId,
                    entity.SinkName,
                    entity.AttemptCount + 1);
                continue;
            }

            var errorMessage = result.IsFailure ? result.Error?.Message : UnknownFailureMessage;
            entity.LastAttemptAtUtc = nowUtc;
            entity.LastErrorMessage = errorMessage;
            entity.AttemptCount = Math.Max(entity.AttemptCount + 1, 1);

            if (entity.AttemptCount >= options.MaxAttempts)
            {
                context.FrameExportRetries.Remove(entity);
                _logger.LogError(
                    "Frame export retry abandoned for frame {FrameId} via sink {Sink} after {Attempts} attempt(s). Last error: {Error}",
                    entity.FrameId,
                    entity.SinkName,
                    entity.AttemptCount,
                    errorMessage ?? "n/a");
                continue;
            }

            entity.NextAttemptAtUtc = CalculateNextAttempt(nowUtc, options, entity.AttemptCount);
            _logger.LogWarning(
                "Frame export retry deferred for frame {FrameId} via sink {Sink}. Attempt {Attempt} will run at {NextAttemptUtc}.",
                entity.FrameId,
                entity.SinkName,
                entity.AttemptCount,
                entity.NextAttemptAtUtc);
        }

        await context.SaveChangesAsync(stoppingToken).ConfigureAwait(false);
        await UpdatePendingCountAsync(context, stoppingToken).ConfigureAwait(false);
    }

    private static FrameExportRetryOptions NormalizeOptions(FrameExportRetryOptions options)
    {
        options ??= new FrameExportRetryOptions();
        options.Normalize();
        return options;
    }

    private static bool TryDeserializeMetadata(string metadataJson, out FrameExportMetadata? metadata)
    {
        try
        {
            metadata = JsonSerializer.Deserialize<FrameExportMetadata>(metadataJson, SerializerOptions);
            return metadata is not null;
        }
        catch
        {
            metadata = null;
            return false;
        }
    }

    private DateTimeOffset CalculateNextAttempt(DateTimeOffset baseTimeUtc, FrameExportRetryOptions options, int attemptCount)
    {
        var exponent = Math.Max(0, attemptCount - 1);
        var multiplier = Math.Pow(options.BackoffMultiplier, exponent);
        var delay = TimeSpan.FromTicks((long)Math.Clamp(options.InitialBackoff.Ticks * multiplier, 0, options.MaxBackoff.Ticks));

        if (delay > options.MaxBackoff)
        {
            delay = options.MaxBackoff;
        }

        if (options.MaxJitter > TimeSpan.Zero)
        {
            var jitterTicks = Random.Shared.NextInt64(-options.MaxJitter.Ticks, options.MaxJitter.Ticks + 1);
            var jitter = TimeSpan.FromTicks(jitterTicks);
            delay = delay + jitter;
            if (delay < TimeSpan.Zero)
            {
                delay = TimeSpan.Zero;
            }
            else if (delay > options.MaxBackoff)
            {
                delay = options.MaxBackoff;
            }
        }

        return baseTimeUtc + delay;
    }

    private async Task UpdatePendingCountAsync(SkyMonitorTelemetryContext context, CancellationToken cancellationToken)
    {
        var count = await context.FrameExportRetries.LongCountAsync(cancellationToken).ConfigureAwait(false);
        _metrics.SetPendingRetryCount(count);
    }
}
