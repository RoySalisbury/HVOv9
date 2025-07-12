using System;
using System.Device.Gpio;
using HVO.Iot.Devices; // Your namespace for GpioLimitSwitch
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

        // Add GpioController as singleton (or scoped if you want)
        services.AddSingleton<GpioController>();

        // Add GpioLimitSwitch with parameters
        services.AddSingleton<GpioLimitSwitch>(sp =>
        {
            var gpioController = sp.GetRequiredService<GpioController>();
            var logger = sp.GetRequiredService<ILogger<GpioLimitSwitch>>();

            // Example GPIO pin number 17, adjust as needed
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
