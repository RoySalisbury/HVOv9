using System;
using System.Device.Gpio;
using System.Device.Gpio.Drivers;
using HVO.Iot.Devices; // Your namespace for GpioLimitSwitch
using HVO.Iot.Devices.Abstractions;
using HVO.Iot.Devices.Implementation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using HVO.CLI.RoofController.HostedService;
using HVO.CLI.RoofController.Logic;
using HVO.Iot.Devices.Iot.Devices.Sequent;
using Iot.Device.Common;

namespace HVO.CLI.RoofController;

class Program
{
    static async Task Main(string[] args)
    {
        var hostApplicationBuilder = Host.CreateApplicationBuilder(args);
        ConfigureServices(hostApplicationBuilder.Services, hostApplicationBuilder.Configuration, hostApplicationBuilder.Environment);

        var host = hostApplicationBuilder.Build();
        ConfigureHost(host, hostApplicationBuilder.Environment);

        await host.StartAsync();

        // Do test stuff here ... the background service is still running....
        var roofController = host.Services.GetRequiredService<IRoofControllerServiceV2>();
        var status = roofController.Status;

        for (int i = 0; i < 10; i++)
        {
            roofController.Open();
            Console.WriteLine($"Status: {roofController.Status}");
            await Task.Delay(1000);

            roofController.Close();
            Console.WriteLine($"Status: {roofController.Status}");
            await Task.Delay(1000);

            roofController.Stop();
            Console.WriteLine($"Status: {roofController.Status}");
            await Task.Delay(1000);
        }

        await host.WaitForShutdownAsync();
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment hostEnvironment)
    {
        // Bind the configuration section to the MyServiceSettings class
        services.Configure<RoofControllerOptionsV2>(configuration.GetSection(nameof(RoofControllerOptionsV2)));
        services.Configure<RoofControllerHostOptionsV2>(configuration.GetSection(nameof(RoofControllerHostOptionsV2)));

        services.AddSingleton<FourRelayFourInputHat>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<FourRelayFourInputHat>>();
            return new FourRelayFourInputHat(logger: logger);
        });

        // Register other services for DI
        services.AddSingleton<IRoofControllerServiceV2, RoofControllerServiceV2>();

        // Register your background service
        services.AddHostedService<RoofControllerServiceV2Host>();
    }

    private static void ConfigureHost(IHost host, IHostEnvironment hostEnvironment)
    {
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

        lifetime.ApplicationStarted.Register(() => Console.WriteLine("IHostApplicationLifetime.ApplicationStarted"));
        lifetime.ApplicationStopping.Register(() => Console.WriteLine("IHostApplicationLifetime.Register"));
        lifetime.ApplicationStopped.Register(() => Console.WriteLine("IHostApplicationLifetime.ApplicationStopped"));
    }
}

