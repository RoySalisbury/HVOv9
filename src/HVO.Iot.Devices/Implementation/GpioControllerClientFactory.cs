using System;
using System.Device.Gpio;
using System.IO;
using System.Runtime.InteropServices;
using HVO.Iot.Devices.Abstractions;

namespace HVO.Iot.Devices.Implementation;

#pragma warning disable CS1591
/// <summary>
/// Factory helpers for building <see cref="IGpioControllerClient"/> instances with the same
/// hardware-versus-simulation selection logic we use for I²C devices.
/// </summary>
public static class GpioControllerClientFactory
{
    /// <summary>
    /// Creates a GPIO controller client, automatically choosing between hardware and in-memory
    /// implementations based on environment heuristics.
    /// </summary>
    public static IGpioControllerClient CreateAutoSelecting(bool? useRealHardware = null)
    {
        if (useRealHardware.HasValue)
        {
            if (useRealHardware.Value && TryCreateHardware(out var forcedHardware))
            {
                return forcedHardware;
            }

            return new MemoryGpioControllerClient();
        }

    var envValue = Environment.GetEnvironmentVariable(IGpioControllerClient.UseRealHardwareEnvironmentVariable);
        var envRequestsReal = string.Equals(envValue, "true", StringComparison.OrdinalIgnoreCase);

        if (envRequestsReal && TryCreateHardware(out var envController))
        {
            return envController;
        }

        if (IsRaspberryPi() && TryCreateHardware(out var piController))
        {
            return piController;
        }

        if (TryCreateHardware(out var fallbackHardware))
        {
            return fallbackHardware;
        }

        return new MemoryGpioControllerClient();
    }

    /// <summary>
    /// Attempts to instantiate a hardware-backed controller client.
    /// </summary>
    public static bool TryCreateHardware(out IGpioControllerClient controller)
    {
        try
        {
            controller = new GpioControllerClient(new GpioController(), ownsController: true);
            return true;
        }
        catch
        {
            controller = default!;
            return false;
        }
    }

    private static bool IsRaspberryPi()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return false;
        }

        try
        {
            if (File.Exists("/proc/device-tree/model"))
            {
                var model = File.ReadAllText("/proc/device-tree/model");
                if (model.Contains("Raspberry", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (File.Exists("/proc/cpuinfo"))
            {
                var cpuInfo = File.ReadAllText("/proc/cpuinfo");
                if (cpuInfo.Contains("Raspberry Pi", StringComparison.OrdinalIgnoreCase) ||
                    cpuInfo.Contains("BCM", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
            // ignore detection errors and fall back to simulation
        }

        return false;
    }
}
#pragma warning restore CS1591
