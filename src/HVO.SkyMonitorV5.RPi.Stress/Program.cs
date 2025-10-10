using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using HVO.SkyMonitorV5.RPi.Cameras;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.HostedServices;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Services;
using HVO.SkyMonitorV5.RPi.Storage;
using HVO.SkyMonitorV5.RPi.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.SkyMonitorV5.RPi.Stress;

internal static class Program
{
    private static readonly string[] FilterOrder =
    {
        FrameFilterNames.CardinalDirections,
        FrameFilterNames.ConstellationFigures,
        FrameFilterNames.CelestialAnnotations,
        FrameFilterNames.OverlayText,
        FrameFilterNames.CircularApertureMask,
        FrameFilterNames.DiagnosticsOverlay
    };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = ScenarioOptions.Parse(args);
            var repoRoot = RepositoryLocator.FindRoot();
            var scenarios = ScenarioDefinition.CreateDefaultMatrix();
            if (options.ScenarioFilter is { Count: > 0 })
            {
                scenarios = scenarios
                    .Where(s => options.ScenarioFilter.Contains(s.Name))
                    .ToList();

                if (scenarios.Count == 0)
                {
                    Console.WriteLine("No scenarios matched the provided --scenario filter.");
                    return 1;
                }

                Console.WriteLine($"Scenario filter applied: {string.Join(", ", options.ScenarioFilter)}");
            }
            var results = new List<ScenarioResult>();

            foreach (var scenario in scenarios)
            {
                Console.WriteLine($"=== Scenario: {scenario.Name} ===");
                var result = await ScenarioRunner.RunAsync(repoRoot, scenario, options).ConfigureAwait(false);
                results.Add(result);
                Console.WriteLine();
            }

            ScenarioResultWriter.WriteSummary(repoRoot, options.OutputDirectory, results);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Scenario runner failed: {ex}");
            return 1;
        }
    }

    private sealed record ScenarioOptions(TimeSpan Duration, TimeSpan SampleInterval, string OutputDirectory, IReadOnlySet<string>? ScenarioFilter)
    {
        private const int DefaultDurationMinutes = 5;
        private const int DefaultSampleSeconds = 15;

        public static ScenarioOptions Parse(string[] args)
        {
            var duration = TimeSpan.FromMinutes(DefaultDurationMinutes);
            var sample = TimeSpan.FromSeconds(DefaultSampleSeconds);
            string? outputDir = null;
            var scenarioNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--duration" when i + 1 < args.Length && int.TryParse(args[i + 1], out var minutes):
                        duration = TimeSpan.FromMinutes(Math.Max(1, minutes));
                        i++;
                        break;
                    case "--duration-seconds" when i + 1 < args.Length && int.TryParse(args[i + 1], out var seconds):
                        duration = TimeSpan.FromSeconds(Math.Max(30, seconds));
                        i++;
                        break;
                    case "--sample" when i + 1 < args.Length && int.TryParse(args[i + 1], out var sampleSeconds):
                        sample = TimeSpan.FromSeconds(Math.Max(1, sampleSeconds));
                        i++;
                        break;
                    case "--output" when i + 1 < args.Length:
                        outputDir = args[i + 1];
                        i++;
                        break;
                    case "--scenario" when i + 1 < args.Length:
                        var names = args[i + 1]
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        foreach (var name in names)
                        {
                            scenarioNames.Add(name);
                        }
                        i++;
                        break;
                }
            }

            var resolvedOutput = string.IsNullOrWhiteSpace(outputDir) ? "artifacts/stress" : outputDir;
            return new ScenarioOptions(
                duration,
                sample,
                resolvedOutput,
                scenarioNames.Count > 0 ? scenarioNames : null);
        }
    }

    private sealed record ScenarioDefinition(
        string Name,
        string AdapterKey,
        RigSpec Rig,
        bool BackgroundEnabled,
        RandomizationProfile Randomization,
        SimulatedCpuLoadProfile CpuLoad)
    {
        public static IReadOnlyList<ScenarioDefinition> CreateDefaultMatrix()
        {
            var pressureProfile = RandomizationProfile.HeavyTraffic;
            return new[]
            {
                new ScenarioDefinition("ASI174MM_StackerOff", CameraAdapterTypes.Mock, RigPresets.MockAsi174_Fujinon, BackgroundEnabled: false, pressureProfile, SimulatedCpuLoadProfile.Disabled),
                new ScenarioDefinition("ASI174MM_StackerOn_Pi5", CameraAdapterTypes.Mock, RigPresets.MockAsi174_Fujinon, BackgroundEnabled: true, pressureProfile, CpuProfiles.RaspberryPi5),
                new ScenarioDefinition("ASI174MC_StackerOff", CameraAdapterTypes.MockColor, RigPresets.MockAsi174MC_Fujinon, BackgroundEnabled: false, pressureProfile, SimulatedCpuLoadProfile.Disabled),
                new ScenarioDefinition("ASI174MC_StackerOn_Pi5", CameraAdapterTypes.MockColor, RigPresets.MockAsi174MC_Fujinon, BackgroundEnabled: true, pressureProfile, CpuProfiles.RaspberryPi5)
            };
        }
    }

    private static class CpuProfiles
    {
        public static readonly SimulatedCpuLoadProfile RaspberryPi5 = new(
            Enabled: true,
            BaselineMilliseconds: 95,
            VariabilityMilliseconds: 35,
            SpikeProbability: 0.2,
            SpikeMultiplier: 2.4,
            WorkerCount: 2,
            MaximumMilliseconds: 260,
            RandomSeed: 7149);

        public static readonly SimulatedCpuLoadProfile IntelI5 = new(
            Enabled: true,
            BaselineMilliseconds: 60,
            VariabilityMilliseconds: 20,
            SpikeProbability: 0.12,
            SpikeMultiplier: 1.9,
            WorkerCount: 3,
            MaximumMilliseconds: 190,
            RandomSeed: 5105);
    }

    private sealed record RandomizationProfile(
        double MinExposureFactor,
        double MaxExposureFactor,
        int MinExposureMilliseconds,
        int MaxExposureMilliseconds,
        double MinGainFactor,
        double MaxGainFactor,
        double FilterDelayProbability,
        double MinFilterDelayMilliseconds,
        double MaxFilterDelayMilliseconds)
    {
        public static RandomizationProfile HeavyTraffic { get; } = new(
            MinExposureFactor: 0.2,
            MaxExposureFactor: 2.2,
            MinExposureMilliseconds: 50,
            MaxExposureMilliseconds: 2000,
            MinGainFactor: 0.8,
            MaxGainFactor: 1.4,
            FilterDelayProbability: 0.65,
            MinFilterDelayMilliseconds: 50,
            MaxFilterDelayMilliseconds: 450);
    }

    private sealed record MetricSample(
        DateTimeOffset Timestamp,
        double PrivateMemoryMb,
        double WorkingSetMb,
        double GcHeapMb,
        double QueueDepth,
        double QueueMemoryMb,
        double? StackDurationMs,
        double? FilterDurationMs,
        double? QueueLatencyMs,
        int? FramesStacked,
        int? QueueCapacity,
        double? QueueFillPercent,
        double? PeakQueueFillPercent);

    private sealed record ScenarioResult(
        string ScenarioName,
        TimeSpan Duration,
        double CpuUtilisationPercent,
        double AveragePrivateMemoryMb,
        double PeakPrivateMemoryMb,
        double AverageWorkingSetMb,
        double PeakWorkingSetMb,
        double AverageGcHeapMb,
        double PeakGcHeapMb,
        double AverageQueueDepth,
        double PeakQueueDepth,
        double AverageQueueMemoryMb,
        double PeakQueueMemoryMb,
        int TotalSamples,
        int FramesObserved,
        string MetricsFilePath,
        SimulatedCpuLoadProfile CpuLoadProfile);

    private static class ScenarioRunner
    {
        public static async Task<ScenarioResult> RunAsync(string repoRoot, ScenarioDefinition scenario, ScenarioOptions options)
        {
            var overrides = BuildOverrides(repoRoot, scenario);
            var configuration = BuildConfiguration(repoRoot, overrides);

            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddSimpleConsole(console =>
            {
                console.SingleLine = true;
                console.TimestampFormat = "HH:mm:ss ";
            })
            .SetMinimumLevel(LogLevel.Information)
            .AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning)
            .AddFilter("Microsoft.AspNetCore.DataProtection", LogLevel.Warning));

            InvokeConfigureServices(services, configuration);

            services.Replace(ServiceDescriptor.Singleton<IExposureController>(sp =>
                new RandomizedExposureController(
                    sp.GetRequiredService<IOptionsMonitor<CameraPipelineOptions>>(),
                    scenario.Randomization)));

            services.Replace(ServiceDescriptor.Singleton<IFrameFilterPipeline>(sp =>
                new RandomDelayFrameFilterPipeline(
                    sp.GetRequiredService<FrameFilterPipeline>(),
                    scenario.Randomization,
                    sp.GetService<ILogger<RandomDelayFrameFilterPipeline>>())));

            if (scenario.BackgroundEnabled && scenario.CpuLoad.Enabled)
            {
                services.Replace(ServiceDescriptor.Singleton<IFrameStacker>(sp =>
                {
                    var inner = new RollingFrameStacker(sp.GetService<ILogger<RollingFrameStacker>>());
                    return new SimulatedCpuLoadFrameStacker(
                        inner,
                        scenario.CpuLoad,
                        sp.GetService<ILogger<SimulatedCpuLoadFrameStacker>>());
                }));
            }

            await using var provider = services.BuildServiceProvider();

            var hostedServices = provider.GetServices<IHostedService>().ToList();
            var background = provider.GetRequiredService<BackgroundFrameStackerService>();
            var capture = hostedServices.OfType<AllSkyCaptureService>().FirstOrDefault()
                ?? throw new InvalidOperationException("AllSkyCaptureService not registered as hosted service.");

            var statusStore = provider.GetRequiredService<IFrameStateStore>();

            using var cts = new CancellationTokenSource();
            var runDuration = options.Duration;
            var sampleInterval = options.SampleInterval;

            var process = Process.GetCurrentProcess();
            var initialCpu = process.TotalProcessorTime;
            var stopwatch = Stopwatch.StartNew();

            await background.StartAsync(CancellationToken.None).ConfigureAwait(false);
            await capture.StartAsync(CancellationToken.None).ConfigureAwait(false);

            var samples = new ConcurrentBag<MetricSample>();
            var framesObserved = 0;
            DateTimeOffset? lastFrameTimestamp = null;

            using var sampleCts = new CancellationTokenSource();
            var samplingTask = Task.Run(async () =>
            {
                while (!sampleCts.IsCancellationRequested)
                {
                    await Task.Delay(sampleInterval, sampleCts.Token).ConfigureAwait(false);
                    process.Refresh();
                    var status = statusStore.GetStatus();
                    var backgroundStatus = status.BackgroundStacker;

                    if (status.LastFrameTimestamp is { } frameTimestamp && frameTimestamp != lastFrameTimestamp)
                    {
                        framesObserved++;
                        lastFrameTimestamp = frameTimestamp;
                    }

                    samples.Add(new MetricSample(
                        Timestamp: DateTimeOffset.UtcNow,
                        PrivateMemoryMb: process.PrivateMemorySize64 / 1024d / 1024d,
                        WorkingSetMb: process.WorkingSet64 / 1024d / 1024d,
                        GcHeapMb: GC.GetTotalMemory(false) / 1024d / 1024d,
                        QueueDepth: backgroundStatus?.QueueDepth ?? 0,
                        QueueMemoryMb: backgroundStatus?.QueueMemoryMegabytes ?? 0,
                        StackDurationMs: backgroundStatus?.LastStackMilliseconds,
                        FilterDurationMs: backgroundStatus?.LastFilterMilliseconds,
                        QueueLatencyMs: backgroundStatus?.LastQueueLatencyMilliseconds,
                        FramesStacked: status.ProcessedFrame?.FramesStacked,
                        QueueCapacity: backgroundStatus?.QueueCapacity,
                        QueueFillPercent: backgroundStatus?.QueueFillPercentage,
                        PeakQueueFillPercent: backgroundStatus?.PeakQueueFillPercentage));
                }
            }, sampleCts.Token);

            var cpuProfileDescription = DescribeCpuProfile(scenario.CpuLoad);
            Console.WriteLine($"Running for {runDuration.TotalMinutes:F1} minutes (background stacker {(scenario.BackgroundEnabled ? "enabled" : "disabled")}, CPU load {cpuProfileDescription}).");
            while (stopwatch.Elapsed < runDuration)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
            }

            sampleCts.Cancel();
            try
            {
                await samplingTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected on cancellation
            }

            await capture.StopAsync(CancellationToken.None).ConfigureAwait(false);
            await background.StopAsync(CancellationToken.None).ConfigureAwait(false);

            stopwatch.Stop();
            process.Refresh();

            var cpuDelta = process.TotalProcessorTime - initialCpu;
            var cpuPercent = cpuDelta.TotalMilliseconds / (Environment.ProcessorCount * stopwatch.Elapsed.TotalMilliseconds) * 100.0;

            var orderedSamples = samples.OrderBy(sample => sample.Timestamp).ToList();
            var aggregate = Aggregate(orderedSamples);

            var metricsPath = ScenarioResultWriter.WriteScenarioMetrics(repoRoot, options.OutputDirectory, scenario.Name, orderedSamples);

            return new ScenarioResult(
                ScenarioName: scenario.Name,
                Duration: stopwatch.Elapsed,
                CpuUtilisationPercent: cpuPercent,
                AveragePrivateMemoryMb: aggregate.AveragePrivateMemoryMb,
                PeakPrivateMemoryMb: aggregate.PeakPrivateMemoryMb,
                AverageWorkingSetMb: aggregate.AverageWorkingSetMb,
                PeakWorkingSetMb: aggregate.PeakWorkingSetMb,
                AverageGcHeapMb: aggregate.AverageGcHeapMb,
                PeakGcHeapMb: aggregate.PeakGcHeapMb,
                AverageQueueDepth: aggregate.AverageQueueDepth,
                PeakQueueDepth: aggregate.PeakQueueDepth,
                AverageQueueMemoryMb: aggregate.AverageQueueMemoryMb,
                PeakQueueMemoryMb: aggregate.PeakQueueMemoryMb,
                TotalSamples: orderedSamples.Count,
                FramesObserved: framesObserved,
                MetricsFilePath: metricsPath,
                CpuLoadProfile: scenario.CpuLoad);
        }

        private static (double AveragePrivateMemoryMb, double PeakPrivateMemoryMb, double AverageWorkingSetMb, double PeakWorkingSetMb, double AverageGcHeapMb, double PeakGcHeapMb, double AverageQueueDepth, double PeakQueueDepth, double AverageQueueMemoryMb, double PeakQueueMemoryMb) Aggregate(IReadOnlyCollection<MetricSample> samples)
        {
            if (samples.Count == 0)
            {
                return (0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            }

            double Sum(Func<MetricSample, double> selector) => samples.Sum(selector);
            double Max(Func<MetricSample, double> selector) => samples.Max(selector);

            var averagePrivate = Sum(s => s.PrivateMemoryMb) / samples.Count;
            var averageWorking = Sum(s => s.WorkingSetMb) / samples.Count;
            var averageGc = Sum(s => s.GcHeapMb) / samples.Count;
            var averageQueueDepth = Sum(s => s.QueueDepth) / samples.Count;
            var averageQueueMemory = Sum(s => s.QueueMemoryMb) / samples.Count;

            return (
                averagePrivate,
                Max(s => s.PrivateMemoryMb),
                averageWorking,
                Max(s => s.WorkingSetMb),
                averageGc,
                Max(s => s.GcHeapMb),
                averageQueueDepth,
                Max(s => s.QueueDepth),
                averageQueueMemory,
                Max(s => s.QueueMemoryMb));
        }

        private static IConfiguration BuildConfiguration(string repoRoot, IDictionary<string, string?> overrides)
        {
            var appSettingsPath = Path.Combine(repoRoot, "src", "HVO.SkyMonitorV5.RPi");

            var builder = new ConfigurationBuilder()
                .SetBasePath(appSettingsPath)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddInMemoryCollection(overrides);

            return builder.Build();
        }

        private static IDictionary<string, string?> BuildOverrides(string repoRoot, ScenarioDefinition scenario)
        {
            var overrides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["CameraPipeline:EnableStacking"] = "true",
                ["CameraPipeline:StackingFrameCount"] = "8",
                ["CameraPipeline:StackingBufferMinimumFrames"] = "180",
                ["CameraPipeline:StackingBufferIntegrationSeconds"] = "180",
                ["CameraPipeline:EnableImageOverlays"] = "true",
                ["CameraPipeline:CaptureIntervalMilliseconds"] = "250",
                ["CameraPipeline:DayExposureMilliseconds"] = "600",
                ["CameraPipeline:NightExposureMilliseconds"] = "600",
                ["CameraPipeline:DayGain"] = "180",
                ["CameraPipeline:NightGain"] = "220",
                ["CameraPipeline:ProcessedImageEncoding:Format"] = "Png",
                ["CameraPipeline:ProcessedImageEncoding:Quality"] = "100",
                ["CameraPipeline:BackgroundStacker:Enabled"] = scenario.BackgroundEnabled ? "true" : "false",
                ["CameraPipeline:BackgroundStacker:QueueCapacity"] = scenario.BackgroundEnabled ? "48" : "24",
                ["CameraPipeline:BackgroundStacker:OverflowPolicy"] = "Block",
                ["CameraPipeline:BackgroundStacker:CompressionMode"] = "None",
                ["CameraPipeline:BackgroundStacker:RestartDelaySeconds"] = "5",
                ["CameraPipeline:BackgroundStacker:AdaptiveQueue:Enabled"] = scenario.BackgroundEnabled ? "true" : "false",
                ["CameraPipeline:BackgroundStacker:AdaptiveQueue:MinCapacity"] = "24",
                ["CameraPipeline:BackgroundStacker:AdaptiveQueue:MaxCapacity"] = "80",
                ["CameraPipeline:BackgroundStacker:AdaptiveQueue:IncreaseStep"] = "8",
                ["CameraPipeline:BackgroundStacker:AdaptiveQueue:DecreaseStep"] = "12",
                ["CameraPipeline:BackgroundStacker:AdaptiveQueue:ScaleUpThresholdPercent"] = "82",
                ["CameraPipeline:BackgroundStacker:AdaptiveQueue:ScaleDownThresholdPercent"] = "38",
                ["CameraPipeline:BackgroundStacker:AdaptiveQueue:EvaluationWindowSeconds"] = "12",
                ["CameraPipeline:BackgroundStacker:AdaptiveQueue:CooldownSeconds"] = "30"
            };

            for (var i = 0; i < FilterOrder.Length; i++)
            {
                overrides[$"CameraPipeline:Filters:{i}:Name"] = FilterOrder[i];
                overrides[$"CameraPipeline:Filters:{i}:Enabled"] = "true";
                overrides[$"CameraPipeline:Filters:{i}:Order"] = (i + 1).ToString(CultureInfo.InvariantCulture);
            }

            overrides["AllSkyCameras:0:Name"] = scenario.Name;
            overrides["AllSkyCameras:0:Adapter"] = scenario.AdapterKey;
            overrides["AllSkyCameras:0:Rig:Name"] = scenario.Rig.Name;
            overrides["AllSkyCameras:0:Rig:Sensor:WidthPx"] = scenario.Rig.Sensor.WidthPx.ToString(CultureInfo.InvariantCulture);
            overrides["AllSkyCameras:0:Rig:Sensor:HeightPx"] = scenario.Rig.Sensor.HeightPx.ToString(CultureInfo.InvariantCulture);
            overrides["AllSkyCameras:0:Rig:Sensor:PixelSizeMicrons"] = scenario.Rig.Sensor.PixelSizeMicrons.ToString(CultureInfo.InvariantCulture);
            overrides["AllSkyCameras:0:Rig:Lens:Model"] = scenario.Rig.Lens.Model.ToString();
            overrides["AllSkyCameras:0:Rig:Lens:FocalLengthMm"] = scenario.Rig.Lens.FocalLengthMm.ToString(CultureInfo.InvariantCulture);
            overrides["AllSkyCameras:0:Rig:Lens:FovXDeg"] = scenario.Rig.Lens.FovXDeg.ToString(CultureInfo.InvariantCulture);
            if (scenario.Rig.Lens.FovYDeg is { } fovY)
            {
                overrides["AllSkyCameras:0:Rig:Lens:FovYDeg"] = fovY.ToString(CultureInfo.InvariantCulture);
            }
            overrides["AllSkyCameras:0:Rig:Lens:RollDeg"] = scenario.Rig.Lens.RollDeg.ToString(CultureInfo.InvariantCulture);
            overrides["AllSkyCameras:0:Rig:Lens:Name"] = scenario.Rig.Lens.Name;
            overrides["AllSkyCameras:0:Rig:Lens:Kind"] = scenario.Rig.Lens.Kind.ToString();

            if (scenario.Rig.Descriptor is { } descriptor)
            {
                overrides["AllSkyCameras:0:Rig:Descriptor:Manufacturer"] = descriptor.Manufacturer;
                overrides["AllSkyCameras:0:Rig:Descriptor:Model"] = descriptor.Model;
                overrides["AllSkyCameras:0:Rig:Descriptor:DriverVersion"] = descriptor.DriverVersion;
                overrides["AllSkyCameras:0:Rig:Descriptor:AdapterName"] = descriptor.AdapterName;
                var capabilityIndex = 0;
                foreach (var capability in descriptor.Capabilities)
                {
                    overrides[$"AllSkyCameras:0:Rig:Descriptor:Capabilities:{capabilityIndex++}"] = capability;
                }
            }

            var hygPath = Path.Combine(repoRoot, "src", "HVO.SkyMonitorV5.RPi", "Data", "hyg_v42.sqlite");
            var constellationsPath = Path.Combine(repoRoot, "src", "HVO.SkyMonitorV5.RPi", "Data", "ConstellationLines.sqlite");
            overrides["ConnectionStrings:HygDatabase"] = $"Data Source={hygPath}";
            overrides["ConnectionStrings:ConstellationDatabase"] = $"Data Source={constellationsPath}";

            return overrides;
        }

        private static void InvokeConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            var programType = typeof(HVO.SkyMonitorV5.RPi.Program);
            var method = programType.GetMethod("ConfigureServices", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("Unable to locate Program.ConfigureServices via reflection.");
            method.Invoke(null, new object[] { services, configuration });
        }
    }

    private sealed class RandomizedExposureController : IExposureController
    {
        private readonly IOptionsMonitor<CameraPipelineOptions> _optionsMonitor;
        private readonly RandomizationProfile _profile;

        public RandomizedExposureController(IOptionsMonitor<CameraPipelineOptions> optionsMonitor, RandomizationProfile profile)
        {
            _optionsMonitor = optionsMonitor;
            _profile = profile;
        }

        public ExposureSettings CreateNextExposure(CameraConfiguration configuration)
        {
            var options = _optionsMonitor.CurrentValue;
            var baseExposure = options.DayExposureMilliseconds;
            var baseGain = options.DayGain;

            var exposureFactor = SampleDouble(_profile.MinExposureFactor, _profile.MaxExposureFactor);
            var gainFactor = SampleDouble(_profile.MinGainFactor, _profile.MaxGainFactor);

            var exposure = (int)Math.Clamp(baseExposure * exposureFactor, _profile.MinExposureMilliseconds, _profile.MaxExposureMilliseconds);
            var gain = (int)Math.Clamp(baseGain * gainFactor, 0, int.MaxValue);

            return new ExposureSettings(
                ExposureMilliseconds: exposure,
                Gain: gain,
                AutoExposure: false,
                AutoGain: false);
        }

        private static double SampleDouble(double min, double max)
        {
            if (min >= max)
            {
                return min;
            }

            var range = max - min;
            return min + Random.Shared.NextDouble() * range;
        }
    }

    private sealed class RandomDelayFrameFilterPipeline : IFrameFilterPipeline
    {
        private readonly IFrameFilterPipeline _inner;
        private readonly RandomizationProfile _profile;
        private readonly ILogger<RandomDelayFrameFilterPipeline>? _logger;

        public RandomDelayFrameFilterPipeline(IFrameFilterPipeline inner, RandomizationProfile profile, ILogger<RandomDelayFrameFilterPipeline>? logger)
        {
            _inner = inner;
            _profile = profile;
            _logger = logger;
        }

        public async Task<ProcessedFrame> ProcessAsync(FrameStackResult frameStackResult, CameraConfiguration configuration, CancellationToken cancellationToken)
        {
            if (_profile.FilterDelayProbability > 0 && Random.Shared.NextDouble() < _profile.FilterDelayProbability)
            {
                var delayMs = SampleDelayMilliseconds(_profile.MinFilterDelayMilliseconds, _profile.MaxFilterDelayMilliseconds);
                if (delayMs > 0)
                {
                    if (_logger?.IsEnabled(LogLevel.Trace) == true)
                    {
                        _logger.LogTrace("Injecting synthetic filter delay of {DelayMs:F0} ms before filter pipeline.", delayMs);
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(delayMs), cancellationToken).ConfigureAwait(false);
                }
            }

            return await _inner.ProcessAsync(frameStackResult, configuration, cancellationToken).ConfigureAwait(false);
        }

        private static double SampleDelayMilliseconds(double min, double max)
        {
            if (min <= 0 && max <= 0)
            {
                return 0;
            }

            if (min >= max)
            {
                return Math.Max(0, min);
            }

            var range = max - min;
            return min + Random.Shared.NextDouble() * range;
        }
    }

    private static class RepositoryLocator
    {
        public static string FindRoot()
        {
            var directory = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(directory))
            {
                if (File.Exists(Path.Combine(directory, "HVOv9.slnf")) || File.Exists(Path.Combine(directory, "HVOv9.sln")))
                {
                    var candidate = directory;
                    if (!Directory.Exists(Path.Combine(candidate, "src")))
                    {
                        var parentDirectory = Directory.GetParent(candidate)?.FullName;
                        if (parentDirectory is not null && Directory.Exists(Path.Combine(parentDirectory, "src")))
                        {
                            candidate = parentDirectory;
                        }
                    }

                    return candidate;
                }
                var parent = Directory.GetParent(directory)?.FullName;
                if (string.IsNullOrEmpty(parent) || string.Equals(parent, directory, StringComparison.Ordinal))
                {
                    break;
                }

                directory = parent;
            }

            throw new InvalidOperationException("Unable to locate repository root from current directory.");
        }
    }

    private static string DescribeCpuProfile(SimulatedCpuLoadProfile profile)
    {
        if (!profile.Enabled)
        {
            return "disabled";
        }

        var spike = profile.SpikeProbability > 0
            ? $", spike {profile.SpikeProbability:P0} x{profile.SpikeMultiplier:F1}"
            : string.Empty;

        var maximum = profile.MaximumMilliseconds is { } max
            ? $", max {max:F0}ms"
            : string.Empty;

        return $"baseline {profile.BaselineMilliseconds:F0}ms ±{profile.VariabilityMilliseconds:F0}ms, workers {profile.WorkerCount}{spike}{maximum}";
    }

    private static class ScenarioResultWriter
    {
        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

        public static string WriteScenarioMetrics(string repoRoot, string outputFolder, string scenarioName, IReadOnlyCollection<MetricSample> samples)
        {
            var directory = Path.Combine(repoRoot, outputFolder);
            Directory.CreateDirectory(directory);

            var fileName = $"{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}_{scenarioName}.metrics.json";
            var path = Path.Combine(directory, fileName);

            var payload = new
            {
                Scenario = scenarioName,
                GeneratedAt = DateTimeOffset.UtcNow,
                Samples = samples
            };

            File.WriteAllText(path, JsonSerializer.Serialize(payload, SerializerOptions));
            return path;
        }

        public static void WriteSummary(string repoRoot, string outputFolder, IReadOnlyCollection<ScenarioResult> results)
        {
            var directory = Path.Combine(repoRoot, outputFolder);
            Directory.CreateDirectory(directory);

            var summary = new
            {
                GeneratedAt = DateTimeOffset.UtcNow,
                Results = results
            };

            var fileName = $"{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}_stress-summary.json";
            var path = Path.Combine(directory, fileName);
            File.WriteAllText(path, JsonSerializer.Serialize(summary, SerializerOptions));

            Console.WriteLine("=== Summary ===");
            foreach (var result in results)
            {
                var loadLabel = DescribeCpuProfile(result.CpuLoadProfile);
                Console.WriteLine(
                    $"{result.ScenarioName}: CPU {result.CpuUtilisationPercent:F1}% | Private {result.AveragePrivateMemoryMb:F0}/{result.PeakPrivateMemoryMb:F0} MB | " +
                    $"Working {result.AverageWorkingSetMb:F0}/{result.PeakWorkingSetMb:F0} MB | Queue depth avg {result.AverageQueueDepth:F1} peak {result.PeakQueueDepth:F1} | CPU load {loadLabel}");
            }

            Console.WriteLine($"Summary written to {path}");
        }

    }
}
