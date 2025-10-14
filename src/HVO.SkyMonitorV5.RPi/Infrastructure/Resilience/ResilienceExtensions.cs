using System;
using System.IO;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio.Exceptions;
using Polly;

namespace HVO.SkyMonitorV5.RPi.Infrastructure.Resilience;

public interface IFrameExportResiliencePolicyProvider
{
    IAsyncPolicy CreatePolicy();
}

internal sealed class FrameExportResiliencePolicyProvider : IFrameExportResiliencePolicyProvider
{
    private readonly IOptionsMonitor<FrameExportResilienceOptions> _optionsMonitor;
    private readonly ILogger<FrameExportResiliencePolicyProvider> _logger;

    public FrameExportResiliencePolicyProvider(
        IOptionsMonitor<FrameExportResilienceOptions> optionsMonitor,
        ILogger<FrameExportResiliencePolicyProvider> logger)
    {
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IAsyncPolicy CreatePolicy()
    {
        var options = _optionsMonitor.CurrentValue;
        options.Normalize();

        var handled = Policy
            .Handle<MinioException>()
            .Or<TimeoutException>()
            .Or<IOException>()
            .Or<InvalidOperationException>();

        AsyncPolicy retryPolicy = options.RetryCount > 0
            ? handled.WaitAndRetryAsync(
                retryCount: options.RetryCount,
                sleepDurationProvider: attempt => CalculateDelay(attempt, options),
                onRetryAsync: (exception, delay, attempt, context) =>
                {
                    _logger.LogWarning(
                        exception,
                        "Frame export sink retry {Attempt} scheduled after {Delay}.",
                        attempt,
                        delay);
                    return Task.CompletedTask;
                })
            : Policy.NoOpAsync();

        if (options.Timeout is { } timeout && timeout > TimeSpan.Zero)
        {
            var timeoutPolicy = Policy.TimeoutAsync(timeout);
            return Policy.WrapAsync(retryPolicy, timeoutPolicy);
        }

        return retryPolicy;
    }

    private static TimeSpan CalculateDelay(int attempt, FrameExportResilienceOptions options)
    {
        var exponent = Math.Clamp(attempt - 1, 0, 30);
        var multiplier = Math.Pow(2, exponent);
        var baseTicks = (long)Math.Min(options.MaxDelay.Ticks, options.BaseDelay.Ticks * multiplier);
        var delay = TimeSpan.FromTicks(baseTicks);

        if (options.Jitter > TimeSpan.Zero)
        {
            var jitterTicks = Random.Shared.NextInt64(-options.Jitter.Ticks, options.Jitter.Ticks + 1);
            var jitter = TimeSpan.FromTicks(jitterTicks);
            delay += jitter;

            if (delay < TimeSpan.Zero)
            {
                delay = TimeSpan.Zero;
            }
            else if (delay > options.MaxDelay)
            {
                delay = options.MaxDelay;
            }
        }

        return delay;
    }
}
