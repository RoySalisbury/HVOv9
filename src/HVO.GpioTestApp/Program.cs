using System;
using System.Device.Gpio;
using System.Device.Gpio.Drivers;
using HVO.Iot.Devices; // Your namespace for GpioLimitSwitch
using HVO.Iot.Devices.Abstractions;
using HVO.Iot.Devices.Implementation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

class Program
{
    static void Main(string[] args)
    {
        // Setup Dependency Injection container
        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection);

        // Build ServiceProvider

        var serviceProvider = serviceCollection.BuildServiceProvider();

// #pragma warning disable SDGPIO0001        
//         var x2 = new LibGpiodV2Driver(0);
// #pragma warning restore SDGPIO0001

        // Get GpioLimitSwitch from DI
        var limitSwitch = serviceProvider.GetRequiredService<GpioLimitSwitch>();

        // Subscribe to the LimitSwitchTriggered event
        limitSwitch.LimitSwitchTriggered += (sender, eventArgs) =>
        {
            Console.WriteLine($"Limit switch triggered! Pin: {eventArgs.PinNumber}, ChangeType: {eventArgs.ChangeType}, Time: {eventArgs.EventDateTime}");
        };

        Console.WriteLine($"Monitoring GPIO pin {limitSwitch.GpioPinNumber}. Press Enter to exit...");
        Console.ReadLine();

        // Dispose when done
        limitSwitch.Dispose();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Add logging to console
        services.AddLogging(configure =>
        {
            configure.AddConsole();
            configure.SetMinimumLevel(LogLevel.Debug);
        });

        // Configure GPIO Controller - GpioControllerWrapper automatically handles platform detection and controller selection
        services.AddSingleton<IGpioController>(_ => GpioControllerWrapper.CreateAutoSelecting());

        // Add GpioLimitSwitch with parameters
        services.AddSingleton<GpioLimitSwitch>(sp =>
        {
            var gpioController = sp.GetRequiredService<IGpioController>();
            var logger = sp.GetRequiredService<ILogger<GpioLimitSwitch>>();

            // Example GPIO pin number 16, adjust as needed
            return new GpioLimitSwitch(
                gpioController: gpioController,
                gpioPinNumber: 16,
                isPullup: true,
                hasExternalResistor: false,
                debounceTime: TimeSpan.FromMilliseconds(50),
                logger: logger);
        });
    }
}
