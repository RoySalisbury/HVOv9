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
        await host.WaitForShutdownAsync();
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment hostEnvironment)
    {
        // Bind the configuration section to the MyServiceSettings class

        services.AddSingleton<FourRelayFourInputHat>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<FourRelayFourInputHat>>();
            return new FourRelayFourInputHat(logger: logger);
        });

    }

    private static void ConfigureHost(IHost host, IHostEnvironment hostEnvironment)
    {
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

        lifetime.ApplicationStarted.Register(() => Console.WriteLine("IHostApplicationLifetime.ApplicationStarted"));
        lifetime.ApplicationStopping.Register(() => Console.WriteLine("IHostApplicationLifetime.Register"));
        lifetime.ApplicationStopped.Register(() => Console.WriteLine("IHostApplicationLifetime.ApplicationStopped"));
    }
}

