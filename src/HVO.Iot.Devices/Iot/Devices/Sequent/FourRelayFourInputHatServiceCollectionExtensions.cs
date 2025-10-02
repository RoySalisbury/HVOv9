using System;
using HVO.Iot.Devices.Abstractions;
using HVO.Iot.Devices.Implementation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HVO.Iot.Devices.Iot.Devices.Sequent;

#pragma warning disable CS1591
/// <summary>
/// Dependency injection helpers for registering the Four Relay / Four Input HAT.
/// </summary>
public static class FourRelayFourInputHatServiceCollectionExtensions
{
    public static IServiceCollection AddFourRelayFourInputHat(this IServiceCollection services, Action<FourRelayFourInputHatOptions>? configure = null)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));

        var options = new FourRelayFourInputHatOptions();
        configure?.Invoke(options);

        if (options.Stack < 0 || options.Stack > FourRelayFourInputHatOptions.MaxStackLevel)
        {
            throw new ArgumentOutOfRangeException(nameof(options.Stack), options.Stack, $"Stack level must be between 0 and {FourRelayFourInputHatOptions.MaxStackLevel}.");
        }

        services.AddSingleton<IFourRelayFourInputHat>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<FourRelayFourInputHat>>();
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
                var address = options.BaseAddress + options.Stack;
                client = new I2cRegisterClient(options.I2cBusId, address, options.PostTransactionDelayMs);
                ownsClient = true;
            }

            return new FourRelayFourInputHat(client, ownsClient, logger)
            {
                DigitalInputPollInterval = options.DigitalInputPollInterval
            };
        });

        services.AddSingleton(sp => (FourRelayFourInputHat)sp.GetRequiredService<IFourRelayFourInputHat>());

        return services;
    }
}

/// <summary>
/// Configurable options for registering the Four Relay / Four Input HAT.
/// </summary>
public sealed class FourRelayFourInputHatOptions
{
    internal const int MaxStackLevel = 7;

    public int Stack { get; set; }
    public int BaseAddress { get; set; } = 0x0E;
    public int I2cBusId { get; set; } = 1;
    public int PostTransactionDelayMs { get; set; } = 15;
    public TimeSpan DigitalInputPollInterval { get; set; } = TimeSpan.FromMilliseconds(25);

    public bool UseSimulation { get; set; }
    public Func<IServiceProvider, II2cRegisterClient>? SimulationClientFactory { get; set; }
    public bool OwnsClientFromSimulationFactory { get; set; } = true;

    public Func<IServiceProvider, II2cRegisterClient>? ClientFactory { get; set; }
    public bool OwnsClientFromFactory { get; set; }
}
#pragma warning restore CS1591
