using Asp.Versioning;
using System;
using System.Collections.Generic;
using HVO.SkyMonitorV5.RPi.Cameras;
using HVO.SkyMonitorV5.RPi.Components;
using HVO.SkyMonitorV5.RPi.Data;
using HVO.SkyMonitorV5.RPi.HostedServices;
using HVO.SkyMonitorV5.RPi.Middleware;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Pipeline.Filters;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Services;
using HVO.SkyMonitorV5.RPi.Storage;
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
        var hygConnectionString = BuildSqliteConnectionString(configuration.GetConnectionString("HygDatabase"));
        var constellationConnectionString = BuildSqliteConnectionString(configuration.GetConnectionString("ConstellationDatabase"));

        services.AddDbContext<HygContext>(opt => opt.UseSqlite(hygConnectionString));

        services.AddDbContextFactory<ConstellationCatalogContext>(options =>
        {
            options.UseSqlite(constellationConnectionString);
        });

        services.AddMemoryCache(options =>
        {
            options.SizeLimit = 256;
        });

        services.AddSingleton<ICelestialProjector, CelestialProjector>();

        services.AddScoped<SkyMonitorRepository>();
        services.AddScoped<IStarRepository>(sp => sp.GetRequiredService<SkyMonitorRepository>());
        services.AddScoped<IPlanetRepository>(sp => sp.GetRequiredService<SkyMonitorRepository>());
        services.AddScoped<IConstellationCatalog>(sp => sp.GetRequiredService<SkyMonitorRepository>());

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
            context.ProblemDetails.Extensions["timestamp"] = DateTimeOffset.UtcNow;

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
            .Bind(configuration.GetSection("CameraPipeline"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ObservatoryLocationOptions>()
            .Bind(configuration.GetSection("ObservatoryLocation"))
            .ValidateDataAnnotations()
            .Validate(static options =>
                !double.IsNaN(options.LatitudeDegrees) && !double.IsNaN(options.LongitudeDegrees),
                "Observatory location must include both latitude and longitude values.")
            .ValidateOnStart();

        services.AddOptions<StarCatalogOptions>()
            .Bind(configuration.GetSection(StarCatalogOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<CardinalDirectionsOptions>()
            .Bind(configuration.GetSection(CardinalDirectionsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<CircularApertureMaskOptions>()
            .Bind(configuration.GetSection(CircularApertureMaskOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<CelestialAnnotationsOptions>()
            .Bind(configuration.GetSection(CelestialAnnotationsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ConstellationFigureOptions>()
            .Bind(configuration.GetSection(ConstellationFigureOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<DiagnosticsOverlayOptions>()
            .Bind(configuration.GetSection(DiagnosticsOverlayOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IFrameStateStore, FrameStateStore>();

        services.AddSingleton<IExposureController, AdaptiveExposureController>();
        services.AddSingleton<IFrameStacker, RollingFrameStacker>();
        services.AddSingleton<BackgroundFrameStackerService>();
        services.AddSingleton<IBackgroundFrameStacker>(sp => sp.GetRequiredService<BackgroundFrameStackerService>());
        services.AddHostedService(sp => sp.GetRequiredService<BackgroundFrameStackerService>());

        services.AddSingleton<IFrameFilter, CardinalDirectionsFilter>();
        services.AddSingleton<IFrameFilter, ConstellationFigureFilter>();
        services.AddSingleton<IFrameFilter, CelestialAnnotationsFilter>();
        services.AddSingleton<IFrameFilter, OverlayTextFilter>();
        services.AddSingleton<IFrameFilter, CircularApertureMaskFilter>();
    services.AddSingleton<IFrameFilter, DiagnosticsOverlayFilter>();

        services.AddSingleton<FrameFilterPipeline>();
        services.AddSingleton<IFrameFilterPipeline>(sp => sp.GetRequiredService<FrameFilterPipeline>());
        services.AddSingleton<IDiagnosticsService, DiagnosticsService>();

        RegisterCameraAdapters(services, configuration);

        services.AddHostedService<AllSkyCaptureService>();
        
        services.AddOpenTelemetry()
            .WithMetrics(builder =>
            {
                builder.ConfigureResource(resourceBuilder => resourceBuilder.AddService(
                    serviceName: "HVO.SkyMonitorV5.RPi",
                    serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0"));

                builder.AddMeter("HVO.SkyMonitor.BackgroundStacker");
                builder.AddPrometheusExporter();
            });
    }

    private static void RegisterCameraAdapters(IServiceCollection services, IConfiguration configuration)
    {
        var cameraConfigurations = configuration
            .GetSection(CameraAdapterOptions.SectionName)
            .Get<IReadOnlyList<CameraAdapterOptions>>() ?? Array.Empty<CameraAdapterOptions>();

        if (cameraConfigurations.Count == 0)
        {
            throw new InvalidOperationException("No all-sky camera adapters are configured. Add at least one entry under 'AllSkyCameras'.");
        }

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var camera in cameraConfigurations)
        {
            if (camera is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(camera.Name))
            {
                throw new InvalidOperationException("Each camera adapter must specify a non-empty Name.");
            }

            var cameraName = camera.Name.Trim();

            if (!seenNames.Add(cameraName))
            {
                throw new InvalidOperationException($"Duplicate camera adapter name '{cameraName}' detected. Each adapter must have a unique name.");
            }

            var rigSpec = camera.Rig?.ToRigSpec() ?? throw new InvalidOperationException($"Camera '{cameraName}' is missing a rig configuration.");
            var adapterKey = string.IsNullOrWhiteSpace(camera.Adapter)
                ? CameraAdapterTypes.Mock
                : camera.Adapter.Trim();

            var normalizedAdapterKey = adapterKey;

            services.AddKeyedSingleton<ICameraAdapter>(cameraName, (sp, _) =>
            {
                if (CameraAdapterTypes.IsMockColor(normalizedAdapterKey))
                {
                    return new MockColorCameraAdapter(
                        sp.GetRequiredService<IOptionsMonitor<ObservatoryLocationOptions>>(),
                        sp.GetRequiredService<IOptionsMonitor<StarCatalogOptions>>(),
                        sp.GetRequiredService<IOptionsMonitor<CardinalDirectionsOptions>>(),
                        sp.GetRequiredService<IServiceScopeFactory>(),
                        rigSpec,
                        sp.GetService<ILoggerFactory>(),
                        sp.GetService<ILogger<MockColorCameraAdapter>>());
                }

                if (CameraAdapterTypes.IsMock(normalizedAdapterKey))
                {
                    return new MockCameraAdapter(
                        sp.GetRequiredService<IOptionsMonitor<ObservatoryLocationOptions>>(),
                        sp.GetRequiredService<IOptionsMonitor<StarCatalogOptions>>(),
                        sp.GetRequiredService<IOptionsMonitor<CardinalDirectionsOptions>>(),
                        sp.GetRequiredService<IServiceScopeFactory>(),
                        rigSpec,
                        sp.GetService<ILogger<MockCameraAdapter>>());
                }

                if (CameraAdapterTypes.IsZwo(normalizedAdapterKey))
                {
                    return new ZwoCameraAdapter(
                        rigSpec,
                        sp.GetService<ILogger<ZwoCameraAdapter>>());
                }

                throw new InvalidOperationException($"Unsupported camera adapter type '{normalizedAdapterKey}' for camera '{cameraName}'.");
            });
        }

        var defaultCameraName = cameraConfigurations[0]?.Name?.Trim();
        if (string.IsNullOrWhiteSpace(defaultCameraName))
        {
            throw new InvalidOperationException("The first camera adapter entry must specify a name so the capture pipeline can resolve the default adapter.");
        }

        services.AddSingleton<ICameraAdapter>(sp => sp.GetRequiredKeyedService<ICameraAdapter>(defaultCameraName));
    }

    private static string BuildSqliteConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'HygDatabase' is not configured.");
        }

        var builder = new SqliteConnectionStringBuilder(connectionString);

        if (!string.IsNullOrWhiteSpace(builder.DataSource) && !Path.IsPathRooted(builder.DataSource))
        {
            builder.DataSource = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, builder.DataSource));
        }

        return builder.ToString();
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
                    timestamp = DateTimeOffset.UtcNow
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
