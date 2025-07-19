using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using HVO.WebSite.RoofControllerV4.Logic;
using HVO.Iot.Devices.Abstractions;
using HVO.Iot.Devices.Implementation;

namespace HVO.WebSite.RoofControllerV4;

/// <summary>
/// Simple test class to verify configuration-based service registration works correctly.
/// This can be used to test the DI logic without running the full application.
/// </summary>
public static class ConfigurationTest
{
    public static void TestServiceRegistration()
    {
        // Test with UseSimulatedEvents = false (should get RoofControllerService)
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RoofControllerOptions:UseSimulatedEvents"] = "false"
            })
            .Build();

        services.AddOptions();
        services.Configure<RoofControllerOptions>(configuration.GetSection(nameof(RoofControllerOptions)));
        services.AddSingleton<ILogger<RoofControllerService>>(_ => LoggerFactory.Create(b => { }).CreateLogger<RoofControllerService>());
        services.AddSingleton<IGpioController>(_ => GpioControllerWrapper.CreateAutoSelecting());

        // Register RoofController based on configuration
        services.AddSingleton<IRoofControllerService>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<RoofControllerService>>();
            var roofControllerOptions = serviceProvider.GetRequiredService<IOptions<RoofControllerOptions>>();
            var gpioController = serviceProvider.GetRequiredService<IGpioController>();

            if (roofControllerOptions.Value.UseSimulatedEvents)
            {
                logger.LogInformation("Using RoofControllerServiceWithSimulatedEvents for development/testing");
                return new RoofControllerServiceWithSimulatedEvents(logger, roofControllerOptions, gpioController);
            }
            else
            {
                logger.LogInformation("Using RoofControllerService for production hardware");
                return new RoofControllerService(logger, roofControllerOptions, gpioController);
            }
        });

        var serviceProvider = services.BuildServiceProvider();
        var roofController = serviceProvider.GetRequiredService<IRoofControllerService>();

        Console.WriteLine($"UseSimulatedEvents=false: Got {roofController.GetType().Name}");

        // Test with UseSimulatedEvents = true (should get RoofControllerServiceWithSimulatedEvents)
        services = new ServiceCollection();
        configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RoofControllerOptions:UseSimulatedEvents"] = "true"
            })
            .Build();

        services.AddOptions();
        services.Configure<RoofControllerOptions>(configuration.GetSection(nameof(RoofControllerOptions)));
        services.AddSingleton<ILogger<RoofControllerService>>(_ => LoggerFactory.Create(b => { }).CreateLogger<RoofControllerService>());
        services.AddSingleton<IGpioController>(_ => GpioControllerWrapper.CreateAutoSelecting());

        // Register RoofController based on configuration (same logic)
        services.AddSingleton<IRoofControllerService>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<RoofControllerService>>();
            var roofControllerOptions = serviceProvider.GetRequiredService<IOptions<RoofControllerOptions>>();
            var gpioController = serviceProvider.GetRequiredService<IGpioController>();

            if (roofControllerOptions.Value.UseSimulatedEvents)
            {
                logger.LogInformation("Using RoofControllerServiceWithSimulatedEvents for development/testing");
                return new RoofControllerServiceWithSimulatedEvents(logger, roofControllerOptions, gpioController);
            }
            else
            {
                logger.LogInformation("Using RoofControllerService for production hardware");
                return new RoofControllerService(logger, roofControllerOptions, gpioController);
            }
        });

        serviceProvider = services.BuildServiceProvider();
        roofController = serviceProvider.GetRequiredService<IRoofControllerService>();

        Console.WriteLine($"UseSimulatedEvents=true: Got {roofController.GetType().Name}");
    }
}
