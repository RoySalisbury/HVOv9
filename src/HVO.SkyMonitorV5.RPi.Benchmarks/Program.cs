using System;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

var job = CreateJobFromEnvironment();

var config = ManualConfig.Create(DefaultConfig.Instance)
    .AddJob(job)
    .WithOptions(ConfigOptions.DisableOptimizationsValidator); // allow debugger-attached or dev runs

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);

static Job CreateJobFromEnvironment()
{
    var job = Job.Default;

    var warmups = TryGetInt("HVO_BENCH_WARMUP_COUNT");
    var iterations = TryGetInt("HVO_BENCH_ITERATION_COUNT");
    var launches = TryGetInt("HVO_BENCH_LAUNCH_COUNT");
    var invocation = TryGetInt("HVO_BENCH_INVOCATION_COUNT");
    var unroll = TryGetInt("HVO_BENCH_UNROLL_FACTOR");

    job = job
        .WithWarmupCount(warmups ?? 1)
        .WithIterationCount(iterations ?? 10);

    if (launches is { } launchCount)
    {
        job = job.WithLaunchCount(launchCount);
    }

    if (invocation is { } invocationCount)
    {
        job = job.WithInvocationCount(invocationCount);
    }

    if (unroll is { } unrollFactor)
    {
        job = job.WithUnrollFactor(unrollFactor);
    }

    return job.WithId("EnvConfiguredJob");
}

static int? TryGetInt(string variableName)
{
    var value = Environment.GetEnvironmentVariable(variableName);
    return int.TryParse(value, out var parsed) ? parsed : null;
}
