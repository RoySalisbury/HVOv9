#nullable enable

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Models;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Pipeline;

/// <summary>
/// Decorates an <see cref="IFrameStacker"/> to inject CPU-bound work, allowing stress scenarios
/// to approximate lower-spec hardware without altering the underlying stacking behaviour.
/// </summary>
public sealed class SimulatedCpuLoadFrameStacker : IFrameStacker, IFrameStackerConfigurationListener
{
    private readonly IFrameStacker _inner;
    private readonly SimulatedCpuLoadProfile _profile;
    private readonly ILogger<SimulatedCpuLoadFrameStacker>? _logger;
    private readonly IFrameStackerConfigurationListener? _configurationListener;
    private readonly Random _random;

    public SimulatedCpuLoadFrameStacker(
        IFrameStacker inner,
        SimulatedCpuLoadProfile profile,
        ILogger<SimulatedCpuLoadFrameStacker>? logger = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _logger = logger;
        _configurationListener = inner as IFrameStackerConfigurationListener;
        _random = profile.RandomSeed.HasValue ? new Random(profile.RandomSeed.Value) : Random.Shared;
    }

    public FrameStackResult Accumulate(CapturedImage frame, CameraConfiguration configuration)
    {
        var result = _inner.Accumulate(frame, configuration);

        if (!_profile.Enabled)
        {
            return result;
        }

        var targetMilliseconds = _profile.NextWorkMilliseconds(_random);
        if (targetMilliseconds <= 0)
        {
            return result;
        }

        var workDuration = TimeSpan.FromMilliseconds(targetMilliseconds);
        ConsumeCpu(workDuration, _profile.WorkerCount);

        if (_logger?.IsEnabled(LogLevel.Trace) == true)
        {
            _logger.LogTrace(
                "Simulated CPU work injected for {DurationMs:F1}ms using {Workers} worker(s).",
                workDuration.TotalMilliseconds,
                _profile.WorkerCount);
        }

        return result;
    }

    public void Reset() => _inner.Reset();

    public void OnConfigurationChanged(CameraConfiguration previousConfiguration, CameraConfiguration currentConfiguration)
        => _configurationListener?.OnConfigurationChanged(previousConfiguration, currentConfiguration);

    private static void ConsumeCpu(TimeSpan duration, int workerCount)
    {
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        workerCount = Math.Max(1, workerCount);

        var deadline = Stopwatch.GetTimestamp() + TimeSpanToStopwatchTicks(duration);

        if (workerCount == 1)
        {
            BurnUntil(deadline);
            return;
        }

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = workerCount
        };

        Parallel.For(0, workerCount, options, _ => BurnUntil(deadline));
    }

    private static void BurnUntil(long deadlineTimestamp)
    {
        double accumulator = 0d;

        while (Stopwatch.GetTimestamp() < deadlineTimestamp)
        {
            accumulator += Math.Sqrt(accumulator + 123.456789);
            if (accumulator > 1_000_000d)
            {
                accumulator = 0d;
            }
        }
    }

    private static long TimeSpanToStopwatchTicks(TimeSpan time)
        => (long)(time.TotalSeconds * Stopwatch.Frequency);
}

/// <summary>
/// Describes the CPU-bound workload characteristics applied by <see cref="SimulatedCpuLoadFrameStacker"/>.
/// </summary>
public sealed record SimulatedCpuLoadProfile(
    bool Enabled,
    double BaselineMilliseconds,
    double VariabilityMilliseconds,
    double SpikeProbability,
    double SpikeMultiplier,
    int WorkerCount,
    double? MaximumMilliseconds = null,
    int? RandomSeed = null)
{
    public static SimulatedCpuLoadProfile Disabled { get; } = new(false, 0, 0, 0, 0, 1);

    public double NextWorkMilliseconds(Random random)
    {
        if (!Enabled)
        {
            return 0;
        }

        var jitter = VariabilityMilliseconds > 0
            ? (random.NextDouble() * 2d - 1d) * VariabilityMilliseconds
            : 0d;

        var duration = Math.Max(0d, BaselineMilliseconds + jitter);

        if (SpikeProbability > 0 && random.NextDouble() < SpikeProbability)
        {
            duration *= Math.Max(1d, SpikeMultiplier);
        }

        if (MaximumMilliseconds is { } max)
        {
            duration = Math.Min(duration, max);
        }

        return duration;
    }
};
