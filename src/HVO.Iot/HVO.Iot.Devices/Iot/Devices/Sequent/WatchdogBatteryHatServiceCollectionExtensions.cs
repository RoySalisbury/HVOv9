using System;
using HVO.Iot.Devices.Abstractions;
using HVO.Iot.Devices.Implementation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HVO.Iot.Devices.Iot.Devices.Sequent;

#pragma warning disable CS1591
/// <summary>
/// Dependency injection helpers for the Watchdog/Battery HAT.
/// </summary>
public static class WatchdogBatteryHatServiceCollectionExtensions
{
    public static IServiceCollection AddWatchdogBatteryHat(this IServiceCollection services, Action<WatchdogBatteryHatOptions>? configure = null)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));

        var options = new WatchdogBatteryHatOptions();
        configure?.Invoke(options);

        services.AddSingleton<IWatchdogBatteryHat>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<WatchdogBatteryHat>>();
            II2cRegisterClient client;
            bool ownsClient;

            if (options.UseSimulation)
            {
                if (options.SimulationClientFactory is null)
                {
                    throw new InvalidOperationException("Simulation mode requires a SimulationClientFactory.");
                }

                client = options.SimulationClientFactory(sp) ?? throw new InvalidOperationException("SimulationClientFactory returned null.");
                ownsClient = options.OwnsClientFromSimulationFactory;
            }
            else if (options.ClientFactory is not null)
            {
                client = options.ClientFactory(sp) ?? throw new InvalidOperationException("ClientFactory returned null.");
                ownsClient = options.OwnsClientFromFactory;
            }
            else
            {
                client = new I2cRegisterClient(options.I2cBusId, options.Address, options.PostTransactionDelayMs);
                ownsClient = true;
            }

            return new WatchdogBatteryHat(client, ownsClient, logger);
        });

        services.AddSingleton(sp => (WatchdogBatteryHat)sp.GetRequiredService<IWatchdogBatteryHat>());

        return services;
    }
}

public sealed class WatchdogBatteryHatOptions
{
    public int I2cBusId { get; set; } = 1;
    public int Address { get; set; } = 0x30;
    public int PostTransactionDelayMs { get; set; } = 15;

    public bool UseSimulation { get; set; }
    public Func<IServiceProvider, II2cRegisterClient>? SimulationClientFactory { get; set; }
    public bool OwnsClientFromSimulationFactory { get; set; } = true;

    public Func<IServiceProvider, II2cRegisterClient>? ClientFactory { get; set; }
    public bool OwnsClientFromFactory { get; set; }
}
#pragma warning restore CS1591
