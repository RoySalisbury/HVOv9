using HVO.NinaClient.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HVO.NinaClient.Extensions;

/// <summary>
/// Extension methods for registering NINA client services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add NINA WebSocket client services to the service collection
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Configuration to bind options from</param>
    /// <param name="configurationSection">Configuration section name (default: "NinaWebSocket")</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddNinaWebSocketClient(
        this IServiceCollection services,
        IConfiguration configuration,
        string configurationSection = "NinaWebSocket")
    {
        // Configure options
        services.Configure<NinaWebSocketOptions>(configuration.GetSection(configurationSection));

        // Register services
        services.TryAddSingleton<INinaWebSocketClient, NinaWebSocketClient>();

        return services;
    }

    /// <summary>
    /// Add NINA WebSocket client services with explicit options
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="options">WebSocket client options</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddNinaWebSocketClient(
        this IServiceCollection services,
        NinaWebSocketOptions options)
    {
        // Register the options instance directly
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(options));

        // Register services
        services.TryAddSingleton<INinaWebSocketClient, NinaWebSocketClient>();

        return services;
    }

    /// <summary>
    /// Add NINA WebSocket client services with configuration action
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Action to configure options</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddNinaWebSocketClient(
        this IServiceCollection services,
        Action<NinaWebSocketOptions> configureOptions)
    {
        // Configure options
        services.Configure(configureOptions);

        // Register services
        services.TryAddSingleton<INinaWebSocketClient, NinaWebSocketClient>();

        return services;
    }

    /// <summary>
    /// Add NINA API client services to the service collection
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Configuration containing NINA client settings</param>
    /// <param name="configurationSectionName">Name of configuration section (default: "NinaApiClient")</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddNinaApiClient(
        this IServiceCollection services,
        IConfiguration configuration,
        string configurationSectionName = "NinaApiClient")
    {
        // Configure options
        services.Configure<NinaApiClientOptions>(
            configuration.GetSection(configurationSectionName));

        // Register HTTP client - let NinaApiClient configure itself to avoid conflicts
        services.AddHttpClient<INinaApiClient, NinaApiClient>();

        return services;
    }

    /// <summary>
    /// Add NINA API client services with explicit configuration
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Action to configure NINA client options</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddNinaApiClient(
        this IServiceCollection services,
        Action<NinaApiClientOptions> configureOptions)
    {
        // Configure options
        services.Configure(configureOptions);

        // Register HTTP client - let NinaApiClient configure itself to avoid conflicts
        services.AddHttpClient<INinaApiClient, NinaApiClient>();

        return services;
    }

    /// <summary>
    /// Add all NINA client services (HTTP API and WebSocket)
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Configuration to bind options from</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddNinaClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Add HTTP API client
        services.AddNinaApiClient(configuration);

        // Add WebSocket client
        services.AddNinaWebSocketClient(configuration);

        return services;
    }
}
