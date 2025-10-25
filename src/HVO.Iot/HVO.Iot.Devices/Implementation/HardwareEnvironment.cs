using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using HVO.Iot.Devices.Abstractions;

namespace HVO.Iot.Devices.Implementation;

internal static class HardwareEnvironment
{
    private const string ForceRaspberryPiEnvVar = "HVO_FORCE_RASPBERRY_PI";
    private const string ContainerBoardHintEnvVar = "HVO_CONTAINER_RPI_HINT";
    private static bool? _isRaspberryPi;

    public static bool IsRaspberryPi()
    {
        if (_isRaspberryPi.HasValue)
        {
            return _isRaspberryPi.Value;
        }

        if (TryReadBooleanEnvironmentVariable(ForceRaspberryPiEnvVar, out var forcedResult))
        {
            _isRaspberryPi = forcedResult;
            return forcedResult;
        }

        if (TryReadBooleanEnvironmentVariable(IGpioControllerClient.UseRealHardwareEnvironmentVariable, out var forceHardware) && forceHardware)
        {
            _isRaspberryPi = true;
            return true;
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            _isRaspberryPi = false;
            return false;
        }

        bool detected = TryMatchDeviceTree(new[]
            {
                "/proc/device-tree/model",
                "/sys/firmware/devicetree/base/model"
            },
            new[] { "Raspberry" })
            || TryMatchDeviceTree(new[]
            {
                "/proc/device-tree/compatible",
                "/sys/firmware/devicetree/base/compatible"
            },
            new[] { "raspberrypi", "brcm,bcm" })
            || TryMatchCpuInfo();

        if (!detected && IsRunningInContainer())
        {
            detected = TryMatchCpuInfo() || CheckContainerHardwareHints();
        }

        _isRaspberryPi = detected;
        return detected;
    }

    internal static void ResetForTests()
    {
        _isRaspberryPi = null;
    }

    private static bool TryMatchDeviceTree(string[] candidatePaths, string[] markers)
    {
        foreach (var path in candidatePaths)
        {
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                var text = ReadFileSafe(path);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                foreach (var marker in markers)
                {
                    if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Ignore IO errors and proceed to other hints.
            }
        }

        return false;
    }

    private static string ReadFileSafe(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var text = Encoding.UTF8.GetString(bytes);
        return text.Replace("\0", string.Empty).Trim();
    }

    private static bool TryMatchCpuInfo()
    {
        const string cpuInfoPath = "/proc/cpuinfo";
        try
        {
            if (!File.Exists(cpuInfoPath))
            {
                return false;
            }

            var cpuInfo = File.ReadAllText(cpuInfoPath);
            return cpuInfo.Contains("Raspberry Pi", StringComparison.OrdinalIgnoreCase) ||
                   cpuInfo.Contains("BCM", StringComparison.OrdinalIgnoreCase) ||
                   cpuInfo.Contains("VideoCore", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsRunningInContainer()
    {
        if (TryReadBooleanEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", out var inContainer) && inContainer)
        {
            return true;
        }

        try
        {
            return File.Exists("/.dockerenv");
        }
        catch
        {
            return false;
        }
    }

    private static bool CheckContainerHardwareHints()
    {
        var architecture = RuntimeInformation.ProcessArchitecture;
        if (architecture != Architecture.Arm && architecture != Architecture.Arm64)
        {
            return false;
        }

        try
        {
            if (File.Exists("/dev/gpiomem") || File.Exists("/dev/gpiochip0"))
            {
                return true;
            }
        }
        catch
        {
            // Ignore permission issues and continue evaluating other hints.
        }

        var hint = Environment.GetEnvironmentVariable(ContainerBoardHintEnvVar);
        if (!string.IsNullOrWhiteSpace(hint) &&
            hint.Contains("raspberry", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool TryReadBooleanEnvironmentVariable(string variableName, out bool parsedValue)
    {
        parsedValue = false;
        if (string.IsNullOrWhiteSpace(variableName))
        {
            return false;
        }

        var value = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return bool.TryParse(value, out parsedValue);
    }
}
