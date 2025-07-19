using Microsoft.Extensions.DependencyInjection;
using HVO.Iot.Devices.Abstractions;
using HVO.Iot.Devices.Implementation;

namespace HVO.Iot.Devices.Tests.TestHelpers;

/// <summary>
/// Helper class for configuring GPIO controller dependency injection in tests.
/// Provides easy switching between mock and real hardware implementations.
/// </summary>
public static class GpioTestConfiguration
{
    /// <summary>
    /// Configures dependency injection for mock GPIO controller testing.
    /// Use this for unit tests and development without requiring actual hardware.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The configured service collection for method chaining.</returns>
    public static IServiceCollection AddMockGpioController(this IServiceCollection services)
    {
        services.AddSingleton<IGpioController, MockGpioController>();
        return services;
    }

    /// <summary>
    /// Configures dependency injection for real GPIO controller hardware.
    /// Use this for integration tests that require actual Raspberry Pi hardware.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The configured service collection for method chaining.</returns>
    public static IServiceCollection AddRealGpioController(this IServiceCollection services)
    {
        services.AddSingleton<IGpioController, GpioControllerWrapper>();
        return services;
    }

    /// <summary>
    /// Creates a service provider configured for mock GPIO testing.
    /// This is a convenience method for quickly setting up test scenarios.
    /// </summary>
    /// <returns>A configured service provider with mock GPIO controller.</returns>
    public static ServiceProvider CreateMockGpioServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddMockGpioController();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Creates a service provider configured for real GPIO hardware testing.
    /// This is a convenience method for integration tests with actual hardware.
    /// Only use this when running tests on actual Raspberry Pi hardware.
    /// </summary>
    /// <returns>A configured service provider with real GPIO controller.</returns>
    public static ServiceProvider CreateRealGpioServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddRealGpioController();
        return services.BuildServiceProvider();
    }
}
