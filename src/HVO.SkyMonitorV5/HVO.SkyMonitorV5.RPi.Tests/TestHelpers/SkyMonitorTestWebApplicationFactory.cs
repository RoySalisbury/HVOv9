using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.Data.Configurations;
using HVO.SkyMonitorV5.Data.Options;
using HVO.SkyMonitorV5.Data.Telemetry;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace HVO.SkyMonitorV5.RPi.Tests.TestHelpers;

/// <summary>
/// Tailored <see cref="WebApplicationFactory{TEntryPoint}"/> that configures the SkyMonitor host for integration testing.
/// Removes long-running hosted services and replaces the configuration store with an in-memory database.
/// </summary>
public sealed class SkyMonitorTestWebApplicationFactory : WebApplicationFactory<ProgramEntryPoint>
{
    private readonly string _dataRoot;
    private readonly string _configurationDatabaseName;

    public SkyMonitorTestWebApplicationFactory()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), "hvo-smv5-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataRoot);
        _configurationDatabaseName = $"SkyMonitorConfigTests-{Guid.NewGuid():N}";
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", "false");

        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["SkyMonitor:Data:OverrideRootPath"] = _dataRoot,
                ["SkyMonitor:Data:PreferContainerRoot"] = "false"
            };

            configurationBuilder.AddInMemoryCollection(settings);
        });

        builder.ConfigureServices(services =>
        {
            var hostedServices = services
                .Where(descriptor => descriptor.ServiceType == typeof(IHostedService))
                .ToList();
            foreach (var descriptor in hostedServices)
            {
                services.Remove(descriptor);
            }

            services.PostConfigure<SkyMonitorDataRootOptions>(options =>
            {
                options.OverrideRootPath = _dataRoot;
                options.PreferContainerRoot = false;
                options.DefaultLocalRoot = _dataRoot;
                options.ContainerRoot = _dataRoot;
            });
        });
    }

    public async Task InitializeConfigurationStoreAsync()
    {
        using var scope = Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SkyMonitorConfigurationContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();
        await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
        await context.Database.EnsureCreatedAsync().ConfigureAwait(false);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        try
        {
            if (Directory.Exists(_dataRoot))
            {
                Directory.Delete(_dataRoot, recursive: true);
            }
        }
        catch
        {
            // ignore cleanup failures in test teardown
        }
    }
}
