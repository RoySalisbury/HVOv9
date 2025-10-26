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
            var address = options.BaseAddress + options.Stack;

            II2cRegisterClient CreateSimulationClient(out bool ownsSimulation)
            {
                if (options.SimulationClientFactory is not null)
                {
                    var simulation = options.SimulationClientFactory(sp) ?? throw new InvalidOperationException("SimulationClientFactory returned null.");
                    ownsSimulation = options.OwnsClientFromSimulationFactory;
                    return simulation;
                }

                ownsSimulation = true;
                return new FourRelayFourInputHatMemoryClient(options.I2cBusId, address);
            }

            II2cRegisterClient client;
            bool ownsClient;

            if (options.UseSimulation)
            {
                client = CreateSimulationClient(out ownsClient);
            }
            else if (options.ClientFactory is not null)
            {
                client = options.ClientFactory(sp) ?? throw new InvalidOperationException("ClientFactory returned null.");
                ownsClient = options.OwnsClientFromFactory;
            }
            else
            {
                var simulationUsed = false;
                var simulationOwns = true;

                II2cRegisterClient SimulationFactory()
                {
                    simulationUsed = true;
                    var simulation = CreateSimulationClient(out var ownsSimulation);
                    simulationOwns = ownsSimulation;
                    return simulation;
                }

                client = I2cRegisterClientFactory.CreateAutoSelecting(
                    options.I2cBusId,
                    address,
                    options.PostTransactionDelayMs,
                    useRealHardware: null,
                    simulationFactory: SimulationFactory);

                ownsClient = simulationUsed ? simulationOwns : true;
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

    /// <summary>
    /// Provides a convenient way to see and customise the default configuration used by RoofController V4.
    /// Callers can optionally mutate the returned instance before passing it to
    /// <see cref="FourRelayFourInputHatServiceCollectionExtensions.AddFourRelayFourInputHat"/>.
    /// </summary>
    public static FourRelayFourInputHatOptions CreateDefault(Action<FourRelayFourInputHatOptions>? configure = null)
    {
        var options = new FourRelayFourInputHatOptions
        {
            Stack = 0,
            BaseAddress = 0x0E,
            I2cBusId = 1,
            PostTransactionDelayMs = 15,
            DigitalInputPollInterval = TimeSpan.FromMilliseconds(25),
            UseSimulation = !HardwareEnvironment.IsRaspberryPi()
        };

        configure?.Invoke(options);
        return options;
    }
}
#pragma warning restore CS1591
