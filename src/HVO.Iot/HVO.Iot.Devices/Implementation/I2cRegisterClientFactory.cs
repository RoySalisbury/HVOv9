using System;
using HVO.Iot.Devices.Abstractions;

namespace HVO.Iot.Devices.Implementation;

#pragma warning disable CS1591
/// <summary>
/// Factory helpers for building <see cref="II2cRegisterClient"/> instances with automatic
/// hardware-versus-simulation selection, mirroring the GPIO factory behaviour.
/// </summary>
public static class I2cRegisterClientFactory
{
    /// <summary>
    /// Creates an auto-selecting I²C register client for the specified bus and address.
    /// </summary>
    /// <param name="busId">The I²C bus identifier.</param>
    /// <param name="address">The device address on the bus.</param>
    /// <param name="postTransactionDelayMs">Optional post-transaction delay applied to hardware access.</param>
    /// <param name="useRealHardware">Optional override to force hardware (true) or simulation (false).</param>
    /// <param name="simulationFactory">Factory used when falling back to simulation. If omitted, a simple
    /// in-memory register client is used.</param>
    public static II2cRegisterClient CreateAutoSelecting(
        int busId,
        int address,
        int postTransactionDelayMs = 15,
        bool? useRealHardware = null,
        Func<II2cRegisterClient>? simulationFactory = null)
    {
        simulationFactory ??= () => new BasicMemoryI2cRegisterClient(busId, address);

        if (useRealHardware.HasValue)
        {
            if (useRealHardware.Value && TryCreateHardware(busId, address, postTransactionDelayMs, out var forcedHardware))
            {
                return forcedHardware;
            }

            return simulationFactory();
        }

        var envValue = Environment.GetEnvironmentVariable(IGpioControllerClient.UseRealHardwareEnvironmentVariable);
        var envRequestsReal = string.Equals(envValue, "true", StringComparison.OrdinalIgnoreCase);
        if (envRequestsReal && TryCreateHardware(busId, address, postTransactionDelayMs, out var envClient))
        {
            return envClient;
        }

        if (HardwareEnvironment.IsRaspberryPi() && TryCreateHardware(busId, address, postTransactionDelayMs, out var piClient))
        {
            return piClient;
        }

        if (TryCreateHardware(busId, address, postTransactionDelayMs, out var fallbackClient))
        {
            return fallbackClient;
        }

        return simulationFactory();
    }

    /// <summary>
    /// Attempts to instantiate a hardware-backed client for the supplied bus/address.
    /// </summary>
    public static bool TryCreateHardware(int busId, int address, int postTransactionDelayMs, out II2cRegisterClient client)
    {
        try
        {
            client = new I2cRegisterClient(busId, address, postTransactionDelayMs);
            return true;
        }
        catch
        {
            client = default!;
            return false;
        }
    }

    private sealed class BasicMemoryI2cRegisterClient : MemoryI2cRegisterClient
    {
        public BasicMemoryI2cRegisterClient(int busId, int address)
            : base(busId, address)
        {
        }
    }
}
#pragma warning restore CS1591
