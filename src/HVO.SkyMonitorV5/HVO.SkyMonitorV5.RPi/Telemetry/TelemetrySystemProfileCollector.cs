using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Telemetry;

internal sealed class TelemetrySystemProfileCollector : ITelemetrySystemProfileCollector
{
    private readonly ILogger<TelemetrySystemProfileCollector> _logger;

    public TelemetrySystemProfileCollector(ILogger<TelemetrySystemProfileCollector> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public TelemetrySystemProfileSnapshot Collect(DateTimeOffset observedAtUtc)
    {
    var machineName = SafeGet(static () => Environment.MachineName);
    var hostName = ResolveHostName(machineName);
    var osDescription = SafeGet(static () => RuntimeInformation.OSDescription);
    var osArchitecture = SafeGet(static () => RuntimeInformation.OSArchitecture.ToString());
    var processArchitecture = SafeGet(static () => RuntimeInformation.ProcessArchitecture.ToString());
    var frameworkDescription = SafeGet(static () => RuntimeInformation.FrameworkDescription);
    var processorCount = SafeGet(static () => Environment.ProcessorCount);
    var totalMemoryMb = TryGetTotalMemoryMegabytes();
    var cpuModel = TryGetCpuModel();
    var hardwareModel = TryGetHardwareModel();
    var isContainerized = TryGetIsContainerized();

        var fingerprint = BuildFingerprint(
            machineName,
            hostName,
            osDescription,
            osArchitecture,
            processArchitecture,
            frameworkDescription,
            processorCount,
            totalMemoryMb,
            cpuModel,
            hardwareModel,
            isContainerized);

        var systemHash = ComputeHash(fingerprint);

        return new TelemetrySystemProfileSnapshot(
            SystemHash: systemHash,
            MachineName: machineName,
            HostName: hostName,
            OperatingSystem: osDescription,
            OsArchitecture: osArchitecture,
            ProcessArchitecture: processArchitecture,
            FrameworkDescription: frameworkDescription,
            ProcessorCount: processorCount,
            TotalMemoryMegabytes: totalMemoryMb,
            CpuModel: cpuModel,
            HardwareModel: hardwareModel,
            IsContainerized: isContainerized,
            AdditionalPropertiesJson: null,
            FirstSeenAtUtc: observedAtUtc,
            LastSeenAtUtc: observedAtUtc);
    }

    private static string ComputeHash(string fingerprint)
    {
        var normalized = fingerprint ?? string.Empty;
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(normalized);
        var hash = sha256.ComputeHash(bytes);
        var builder = new StringBuilder(hash.Length * 2);
        for (var i = 0; i < hash.Length; i++)
        {
            _ = builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static string BuildFingerprint(
        string? machineName,
        string? hostName,
        string? osDescription,
        string? osArchitecture,
        string? processArchitecture,
        string? frameworkDescription,
        int? processorCount,
        double? totalMemoryMb,
        string? cpuModel,
        string? hardwareModel,
        bool? isContainerized)
    {
        var builder = new StringBuilder();
        AppendFingerprintValue(builder, machineName);
        AppendFingerprintValue(builder, hostName);
        AppendFingerprintValue(builder, osDescription);
        AppendFingerprintValue(builder, osArchitecture);
        AppendFingerprintValue(builder, processArchitecture);
        AppendFingerprintValue(builder, frameworkDescription);
        AppendFingerprintValue(builder, processorCount?.ToString(CultureInfo.InvariantCulture));
        AppendFingerprintValue(builder, totalMemoryMb?.ToString("F0", CultureInfo.InvariantCulture));
        AppendFingerprintValue(builder, cpuModel);
        AppendFingerprintValue(builder, hardwareModel);
        AppendFingerprintValue(builder, isContainerized.HasValue ? (isContainerized.Value ? "container" : "baremetal") : null);
        return builder.ToString();
    }

    private static void AppendFingerprintValue(StringBuilder builder, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            builder.Append('|');
            return;
        }

        builder.Append(value.Trim()).Append('|');
    }

    private string? ResolveHostName(string? machineName)
    {
        var host = SafeGet(static () => Environment.GetEnvironmentVariable("HOSTNAME"));
        if (!string.IsNullOrWhiteSpace(host))
        {
            return host?.Trim();
        }

        var dnsHost = SafeGet(Dns.GetHostName);
        if (!string.IsNullOrWhiteSpace(dnsHost))
        {
            return dnsHost?.Trim();
        }

        return machineName;
    }

    private double? TryGetTotalMemoryMegabytes()
    {
        if (OperatingSystem.IsLinux())
        {
            try
            {
                using var reader = new StreamReader("/proc/meminfo");
                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    if (!line.StartsWith("MemTotal:", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var tokens = line.Split(':', StringSplitOptions.TrimEntries);
                    if (tokens.Length < 2)
                    {
                        continue;
                    }

                    var parts = tokens[1].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (parts.Length == 0)
                    {
                        continue;
                    }

                    if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var valueKb))
                    {
                        return valueKb / 1024d;
                    }
                }
            }
            catch (IOException ex)
            {
                _logger.LogTrace(ex, "Failed to read MemTotal from /proc/meminfo.");
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogTrace(ex, "Unauthorized when reading /proc/meminfo for total memory.");
            }
        }

        try
        {
            var info = GC.GetGCMemoryInfo();
            if (info.TotalAvailableMemoryBytes > 0)
            {
                return info.TotalAvailableMemoryBytes / 1024d / 1024d;
            }

            if (info.HighMemoryLoadThresholdBytes > 0)
            {
                return info.HighMemoryLoadThresholdBytes / 1024d / 1024d;
            }
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Failed to read GC memory info for system profile.");
        }

        return null;
    }

    private string? TryGetCpuModel()
    {
        if (OperatingSystem.IsLinux())
        {
            try
            {
                using var reader = new StreamReader("/proc/cpuinfo");
                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    if (line.StartsWith("model name", StringComparison.OrdinalIgnoreCase) || line.StartsWith("Hardware", StringComparison.OrdinalIgnoreCase))
                    {
                        var separatorIndex = line.IndexOf(':');
                        if (separatorIndex < 0)
                        {
                            continue;
                        }

                        var model = line[(separatorIndex + 1)..].Trim();
                        if (!string.IsNullOrEmpty(model))
                        {
                            return model;
                        }
                    }
                }
            }
            catch (IOException ex)
            {
                _logger.LogTrace(ex, "Failed to read CPU model from /proc/cpuinfo.");
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogTrace(ex, "Unauthorized when reading /proc/cpuinfo for CPU model.");
            }
        }

        return SafeGet(static () => Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"));
    }

    private string? TryGetHardwareModel()
    {
        if (OperatingSystem.IsLinux())
        {
            const string deviceTreeModelPath = "/proc/device-tree/model";
            try
            {
                if (File.Exists(deviceTreeModelPath))
                {
                    return File.ReadAllText(deviceTreeModelPath).TrimEnd('\0').Trim();
                }
            }
            catch (IOException ex)
            {
                _logger.LogTrace(ex, "Failed to read hardware model from {DeviceTreeModelPath}.", deviceTreeModelPath);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogTrace(ex, "Unauthorized when reading {DeviceTreeModelPath} for hardware model.", deviceTreeModelPath);
            }
        }

        return null;
    }

    private bool? TryGetIsContainerized()
    {
        var value = SafeGet(static () => Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"));
        if (!string.IsNullOrWhiteSpace(value) && bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        var kubernetesServiceHost = SafeGet(static () => Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST"));
        if (!string.IsNullOrWhiteSpace(kubernetesServiceHost))
        {
            return true;
        }

        return null;
    }

    private T? SafeGet<T>(Func<T> accessor)
    {
        try
        {
            return accessor();
        }
        catch (Exception ex)
        {
            var accessorName = accessor.Method?.Name ?? "unknown_accessor";
            _logger.LogTrace(ex, "Failed to gather system profile value from {Accessor}.", accessorName);
            return default;
        }
    }
}
