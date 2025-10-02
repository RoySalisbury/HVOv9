using System;
using System.Device.Gpio;
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

        if (HardwareEnvironment.IsRaspberryPi() && TryCreateHardware(out var piController))
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
}
#pragma warning restore CS1591
