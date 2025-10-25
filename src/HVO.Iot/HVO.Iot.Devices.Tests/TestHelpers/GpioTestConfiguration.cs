using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using HVO.Iot.Devices.Abstractions;
using HVO.Iot.Devices.Implementation;

namespace HVO.Iot.Devices.Tests.TestHelpers;

/// <summary>
/// Helper class for configuring GPIO controller dependency injection in tests.
/// Provides easy switching between in-memory and real hardware implementations.
/// </summary>
public static class GpioTestConfiguration
{
    /// <summary>
    /// Configures dependency injection for the in-memory GPIO controller client.
    /// Use this for unit tests and development without requiring actual hardware.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The configured service collection for method chaining.</returns>
    public static IServiceCollection AddMemoryGpioControllerClient(this IServiceCollection services)
    {
        services.AddSingleton<IGpioControllerClient>(_ => new MemoryGpioControllerClient());
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Trace));
        return services;
    }


    /// <summary>
    /// Creates a service provider configured for in-memory GPIO testing.
    /// This is a convenience method for quickly setting up test scenarios.
    /// </summary>
    /// <returns>A configured service provider with the in-memory GPIO controller client.</returns>
    public static ServiceProvider CreateMemoryGpioServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddMemoryGpioControllerClient();
        return services.BuildServiceProvider();
    }

}
