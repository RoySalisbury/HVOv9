using Asp.Versioning;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using HVO.SkyMonitorV5.Data.Catalogs.Constellations;
using HVO.SkyMonitorV5.Data.Catalogs.DeepSky;
using HVO.SkyMonitorV5.Data.Catalogs.Hyg;
using HVO.SkyMonitorV5.Data.Configurations;
using HVO.SkyMonitorV5.Data.Extensions;
using HVO.SkyMonitorV5.RPi.Cameras;
using HVO.SkyMonitorV5.RPi.Cameras.Acquisition;
using HVO.SkyMonitorV5.RPi.Cameras.Drivers;
using HVO.SkyMonitorV5.RPi.Components;
using HVO.SkyMonitorV5.RPi.Data;
using HVO.SkyMonitorV5.RPi.HostedServices;
using HVO.SkyMonitorV5.RPi.Middleware;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Pipeline.Filters;
using HVO.SkyMonitorV5.RPi.Catalog;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Services;
using HVO.SkyMonitorV5.RPi.Services.RemoteDispatch;
using HVO.SkyMonitorV5.RPi.Storage;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Scalar.AspNetCore;
using System.IO;
using System.Text.Json.Serialization;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace HVO.SkyMonitorV5.RPi;

/// <summary>
/// Application entry point for the SkyMonitor v5 Raspberry Pi host.
/// </summary>
public static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        ConfigureServices(builder.Services, builder.Configuration);
        ConfigureLogging(builder.Logging);

        var app = builder.Build();
        Configure(app);

        app.Run();
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSkyMonitorDataInfrastructure(configuration);

        services.AddSkyMonitorConfigurationStore(
            relativePath: "configuration/sm-config.db",
            configureOptions: builder => builder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

        services.AddHostedService<ConfigurationStoreBootstrapper>();

        services.AddSkyMonitorSqliteDbContext<HygContext>(
            relativePath: "catalogs/hyg_v42.sqlite",
            configureOptions: builder => builder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking),
            openMode: SqliteOpenMode.ReadOnly,
            enableMigrations: false);

        services.AddSkyMonitorSqliteDbContextFactory<ConstellationCatalogContext>(
            relativePath: "catalogs/ConstellationLines.sqlite",
            openMode: SqliteOpenMode.ReadOnly,
            enableMigrations: false);

        services.AddSkyMonitorSqliteDbContextFactory<DeepSkyCatalogContext>(
            relativePath: "catalogs/deep-sky.sqlite",
            openMode: SqliteOpenMode.ReadOnly,
            enableMigrations: false);

        services.AddMemoryCache(options =>
        {
            options.SizeLimit = 256;
        });

        services.AddSingleton<ICelestialProjector, CelestialProjector>();

        services.AddScoped<SkyMonitorRepository>();
        services.AddScoped<IStarRepository>(sp => sp.GetRequiredService<SkyMonitorRepository>());
        services.AddScoped<IPlanetRepository>(sp => sp.GetRequiredService<SkyMonitorRepository>());
        services.AddScoped<IConstellationCatalog>(sp => sp.GetRequiredService<SkyMonitorRepository>());
    services.AddScoped<IDeepSkyCatalog>(sp => sp.GetRequiredService<SkyMonitorRepository>());

        services.AddRazorComponents()
            .AddInteractiveServerComponents();

        services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            });

        services.AddHttpClient();
        services.AddHttpContextAccessor();

        services.AddExceptionHandler<HvoServiceExceptionHandler>();

        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            context.ProblemDetails.Instance = $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}";
            context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
            var clock = context.HttpContext.RequestServices.GetService<IObservatoryClock>();
            var timestamp = clock?.LocalNow ?? DateTimeOffset.Now;
            context.ProblemDetails.Extensions["timestamp"] = timestamp;

            var activity = context.HttpContext.Features.Get<IHttpActivityFeature>()?.Activity;
            if (activity is not null)
            {
                context.ProblemDetails.Extensions["activityId"] = activity.Id;
            }

            if (context.HttpContext.Request.Headers.TryGetValue("User-Agent", out var userAgent))
            {
                context.ProblemDetails.Extensions["userAgent"] = userAgent.ToString();
            }
        });

        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy("SkyMonitor v5 is running"));

        services.AddOpenApi("v1", options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "HVO SkyMonitor v5 API",
                    Version = "v1.0",
                    Description = "Camera capture and processing pipeline for the Hualapai Valley Observatory SkyMonitor v5 system",
                    Contact = new OpenApiContact
                    {
                        Name = "HVO Engineering",
                        Email = "admin@hualapai-valley-observatory.com"
                    }
                };
                return Task.CompletedTask;
            });
        });

        services.AddEndpointsApiExplorer();

        services.AddApiVersioning(options =>
        {
            options.DefaultApiVersion = new ApiVersion(1, 0);
            options.AssumeDefaultVersionWhenUnspecified = true;
            options.ReportApiVersions = true;
            options.ApiVersionReader = new UrlSegmentApiVersionReader();
        }).AddApiExplorer();

        services.AddOptions<CameraPipelineOptions>()
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ObservatoryLocationOptions>()
            .ValidateDataAnnotations()
            .Validate(static options =>
                !double.IsNaN(options.LatitudeDegrees) && !double.IsNaN(options.LongitudeDegrees),
                "Observatory location must include both latitude and longitude values.")
            .ValidateOnStart();

        services.AddOptions<StarCatalogOptions>()
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<CardinalDirectionsOptions>()
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<CircularApertureMaskOptions>()
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<CelestialAnnotationsOptions>()
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ConstellationFigureOptions>()
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<DiagnosticsOverlayOptions>()
            .Bind(configuration.GetSection(DiagnosticsOverlayOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<DatabaseBackedConfigurationOptionsConfigurator>();
        services.AddSingleton<IConfigureOptions<ObservatoryLocationOptions>>(sp => sp.GetRequiredService<DatabaseBackedConfigurationOptionsConfigurator>());
        services.AddSingleton<IConfigureOptions<AllSkyCatalogOptions>>(sp => sp.GetRequiredService<DatabaseBackedConfigurationOptionsConfigurator>());
        services.AddSingleton<IConfigureOptions<CameraPipelineOptions>>(sp => sp.GetRequiredService<DatabaseBackedConfigurationOptionsConfigurator>());
        services.AddSingleton<IConfigureOptions<CardinalDirectionsOptions>>(sp => sp.GetRequiredService<DatabaseBackedConfigurationOptionsConfigurator>());
        services.AddSingleton<IConfigureOptions<CircularApertureMaskOptions>>(sp => sp.GetRequiredService<DatabaseBackedConfigurationOptionsConfigurator>());
        services.AddSingleton<IConfigureOptions<CelestialAnnotationsOptions>>(sp => sp.GetRequiredService<DatabaseBackedConfigurationOptionsConfigurator>());
        services.AddSingleton<IConfigureOptions<ConstellationFigureOptions>>(sp => sp.GetRequiredService<DatabaseBackedConfigurationOptionsConfigurator>());
        services.AddSingleton<IConfigureOptions<StarCatalogOptions>>(sp => sp.GetRequiredService<DatabaseBackedConfigurationOptionsConfigurator>());

        services.AddSingleton<IFrameStateStore, FrameStateStore>();

        services.AddSingleton<IExposureAnalyzer, SimpleExposureAnalyzer>();
        services.AddSingleton<IExposureController, AdaptiveExposureController>();
        services.AddSingleton<IFrameStacker, RollingFrameStacker>();
        services.AddSingleton<IMinioClientProvider, MinioClientProvider>();
        services.AddSingleton<IRemoteFrameEncoder, SkiaRemoteFrameEncoder>();
        services.AddSingleton<IRemoteFramePublisher, RemoteFramePublisher>();
        services.AddSingleton<BackgroundFrameStackerService>();
        services.AddSingleton<IBackgroundFrameStacker>(sp => sp.GetRequiredService<BackgroundFrameStackerService>());
        services.AddHostedService(sp => sp.GetRequiredService<BackgroundFrameStackerService>());
    services.AddSingleton<RemoteDispatchMetricsObserver>();
    services.AddHostedService(sp => sp.GetRequiredService<RemoteDispatchMetricsObserver>());

        services.AddSingleton<IFrameFilter, CardinalDirectionsFilter>();
        services.AddSingleton<IFrameFilter, ConstellationFigureFilter>();
        services.AddSingleton<IFrameFilter, CelestialAnnotationsFilter>();
        services.AddSingleton<IFrameFilter, OverlayTextFilter>();
        services.AddSingleton<IFrameFilter, CircularApertureMaskFilter>();
        services.AddSingleton<IFrameFilter, DiagnosticsOverlayFilter>();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IObservatoryClock, ObservatoryClock>();

        services.AddOptions<AllSkyCatalogOptions>()
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<AllSkyCatalogRegistry>();
        services.AddSingleton<ICameraCatalog>(sp => sp.GetRequiredService<AllSkyCatalogRegistry>());
        services.AddSingleton<ILensCatalog>(sp => sp.GetRequiredService<AllSkyCatalogRegistry>());
        services.AddSingleton<IRigCatalog>(sp => sp.GetRequiredService<AllSkyCatalogRegistry>());
        services.AddHostedService<CatalogConfigurationReporter>();

        services.AddSingleton<ICameraDriverFactory, CameraDriverFactory>();
        services.AddSingleton<IRigAcquisitionAdapter, RigAcquisitionAdapter>();

        services.AddSingleton<FrameFilterPipeline>();
        services.AddSingleton<IFrameFilterPipeline>(sp => sp.GetRequiredService<FrameFilterPipeline>());
        services.AddSingleton<IDiagnosticsService, DiagnosticsService>();

        RegisterCameraAdapters(services);

    services.AddHostedService<AllSkyCaptureService>();
        
        services.AddOpenTelemetry()
            .WithMetrics(builder =>
            {
                builder.ConfigureResource(resourceBuilder => resourceBuilder.AddService(
                    serviceName: "HVO.SkyMonitorV5.RPi",
                    serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0"));

                builder.AddMeter("HVO.SkyMonitor.BackgroundStacker");
                builder.AddMeter("HVO.SkyMonitor.RemoteDispatch");
                builder.AddPrometheusExporter();
            });
    }

    private static void RegisterCameraAdapters(IServiceCollection services)
    {
        services.AddSingleton<ICameraAdapter>(sp =>
        {
            var configurator = sp.GetRequiredService<DatabaseBackedConfigurationOptionsConfigurator>();
            var adapters = configurator.GetCameraAdapters();

            if (adapters.Count == 0)
            {
                throw new InvalidOperationException("No all-sky camera adapters are configured in the SkyMonitor data store.");
            }

            var catalogOptions = sp.GetRequiredService<IOptionsMonitor<AllSkyCatalogOptions>>().CurrentValue;
            var preferredRigKey = catalogOptions?.Rigs?.ActiveRig;

            var descriptor = adapters
                .FirstOrDefault(adapter =>
                    !string.IsNullOrWhiteSpace(preferredRigKey) &&
                    string.Equals(adapter.RigKey, preferredRigKey, StringComparison.OrdinalIgnoreCase))
                ?? adapters[0];

            if (string.IsNullOrWhiteSpace(descriptor.RigKey))
            {
                throw new InvalidOperationException($"Camera adapter '{descriptor.Name}' must reference a rig catalog entry.");
            }

            var adapterOptions = new CameraAdapterOptions
            {
                Name = descriptor.Name,
                Adapter = descriptor.AdapterType,
                RigCatalog = descriptor.RigKey
            };

            Validator.ValidateObject(adapterOptions, new ValidationContext(adapterOptions), validateAllProperties: true);

            var rigCatalog = sp.GetRequiredService<IRigCatalog>();
            var loggerFactory = sp.GetService<ILoggerFactory>();
            var configLogger = loggerFactory?.CreateLogger("HVO.SkyMonitor.CameraAdapters");
            var rigSpec = adapterOptions.ResolveRig(rigCatalog, configLogger);
            var observatoryClock = sp.GetRequiredService<IObservatoryClock>();

            if (CameraAdapterTypes.IsMockColor(adapterOptions.Adapter))
            {
                return new MockColorCameraAdapter(
                    sp.GetRequiredService<IOptionsMonitor<ObservatoryLocationOptions>>(),
                    sp.GetRequiredService<IOptionsMonitor<StarCatalogOptions>>(),
                    sp.GetRequiredService<IOptionsMonitor<CardinalDirectionsOptions>>(),
                    sp.GetRequiredService<IServiceScopeFactory>(),
                    rigSpec,
                    observatoryClock,
                    loggerFactory,
                    sp.GetService<ILogger<MockColorCameraAdapter>>());
            }

            if (CameraAdapterTypes.IsMock(adapterOptions.Adapter))
            {
                return new MockCameraAdapter(
                    sp.GetRequiredService<IOptionsMonitor<ObservatoryLocationOptions>>(),
                    sp.GetRequiredService<IOptionsMonitor<StarCatalogOptions>>(),
                    sp.GetRequiredService<IOptionsMonitor<CardinalDirectionsOptions>>(),
                    sp.GetRequiredService<IServiceScopeFactory>(),
                    rigSpec,
                    observatoryClock,
                    sp.GetService<ILogger<MockCameraAdapter>>());
            }

            if (CameraAdapterTypes.IsZwo(adapterOptions.Adapter))
            {
                return new ZwoCameraAdapter(
                    rigSpec,
                    observatoryClock,
                    sp.GetService<ILogger<ZwoCameraAdapter>>());
            }

            throw new InvalidOperationException($"Unsupported camera adapter type '{adapterOptions.Adapter}'.");
        });
    }

    private static void ConfigureLogging(ILoggingBuilder logging)
    {
        logging.AddConsole();
        logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
        logging.AddFilter("Microsoft.AspNetCore.DataProtection", LogLevel.Warning);
    }

    private static void Configure(WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        app.MapOpenApi();

        if (app.Environment.IsDevelopment())
        {
            app.MapScalarApiReference();
            app.UseDeveloperExceptionPage();
        }
        else
        {
            app.UseHsts();
        }

        var enableHttpsRedirect = app.Configuration.GetValue("EnableHttpsRedirect", !app.Environment.IsDevelopment());
        if (enableHttpsRedirect)
        {
            app.UseHttpsRedirection();
        }

        app.UseStaticFiles();
        app.UseRouting();
        app.UseAntiforgery();

        app.MapStaticAssets();
        app.MapControllers();
        app.MapPrometheusScrapingEndpoint("/metrics/prometheus");

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = async (context, report) =>
            {
                context.Response.ContentType = "application/json";
                var clock = context.RequestServices.GetService<IObservatoryClock>();
                var payload = new
                {
                    status = report.Status.ToString(),
                    checks = report.Entries.Select(entry => new
                    {
                        name = entry.Key,
                        status = entry.Value.Status.ToString(),
                        description = entry.Value.Description,
                        duration = entry.Value.Duration,
                        tags = entry.Value.Tags
                    }),
                    timestamp = clock?.LocalNow ?? DateTimeOffset.Now
                };

                await context.Response.WriteAsJsonAsync(payload);
            }
        });

        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false
        });

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("database")
        });
    }
}
