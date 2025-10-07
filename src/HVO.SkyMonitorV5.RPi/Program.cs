using Asp.Versioning;
using HVO.SkyMonitorV5.RPi.Cameras;
using HVO.SkyMonitorV5.RPi.Components;
using HVO.SkyMonitorV5.RPi.Data;
using HVO.SkyMonitorV5.RPi.HostedServices;
using HVO.SkyMonitorV5.RPi.Middleware;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Pipeline.Filters;
using HVO.SkyMonitorV5.RPi.Services;
using HVO.SkyMonitorV5.RPi.Storage;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
using Scalar.AspNetCore;
using System.IO;
using System.Text.Json.Serialization;

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

        services.AddDbContext<HygContext>(opt => opt.UseSqlite(hygConnectionString));

// Memory cache
services.AddMemoryCache(options =>
{
    // Optional: limit total cache size (each entry uses Size=1 in our repo)
    options.SizeLimit = 256; // 256 entries; tune to taste
});


        services.AddScoped<IStarRepository>(sp =>
{
    var inner = new HygStarRepository(sp.GetRequiredService<HygContext>());
    var cache = sp.GetRequiredService<IMemoryCache>();
    return new CachedStarRepository(inner, cache,
        absoluteTtl: TimeSpan.FromMinutes(30),
        slidingTtl: TimeSpan.FromMinutes(10));
});        

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

        services.AddOptions<CircularMaskOptions>()
            .Bind(configuration.GetSection(CircularMaskOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

    services.AddSingleton<IFrameStateStore, FrameStateStore>();

    services.AddSingleton<IExposureController, AdaptiveExposureController>();
    services.AddSingleton<IFrameStacker, RollingFrameStacker>();

    services.AddSingleton<IFrameFilter, CardinalDirectionsFilter>();
    services.AddSingleton<IFrameFilter, CelestialAnnotationsFilter>();
    services.AddSingleton<IFrameFilter, OverlayTextFilter>();
    services.AddSingleton<IFrameFilter, CircularMaskFilter>();

    services.AddSingleton<IFrameFilterPipeline, FrameFilterPipeline>();

    services.AddSingleton<ICameraAdapter, MockFisheyeCameraAdapter>();

        services.AddHostedService<AllSkyCaptureService>();
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
