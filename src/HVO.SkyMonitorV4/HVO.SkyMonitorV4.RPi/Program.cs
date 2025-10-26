using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Asp.Versioning;
using Asp.Versioning.Builder;
using HVO.SkyMonitorV4.RPi.Components;
using HVO.SkyMonitorV4.RPi.HostedServices;
using HVO.SkyMonitorV4.RPi.HostedServices.AllSkyCamera;
using HVO.SkyMonitorV4.RPi.HostedServices.AllSkyImageCleanup;
using HVO.SkyMonitorV4.RPi.HostedServices.AllSkyImageSave;
using HVO.SkyMonitorV4.RPi.HostedServices.AllSkyTimelapse;
using HVO.SkyMonitorV4.RPi.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Scalar.AspNetCore;

namespace HVO.SkyMonitorV4.RPi;

/// <summary>
/// Application entry point for the SkyMonitor v4 RPi control site.
/// </summary>
public static class Program
{
    /// <summary>
    /// Application entry point.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        ConfigureServices(builder.Services);

        var app = builder.Build();
        Configure(app);

        app.Run();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddOptions();

        services.AddRazorComponents()
            .AddInteractiveServerComponents();

        services.AddEndpointsApiExplorer();
        services.AddOpenApi("v1");

        services.AddExceptionHandler<HvoServiceExceptionHandler>();

        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            context.ProblemDetails.Instance = $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}";
            context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
            context.ProblemDetails.Extensions["timestamp"] = DateTime.UtcNow;

            if (context.HttpContext.Request.Headers.TryGetValue("User-Agent", out var userAgent))
            {
                context.ProblemDetails.Extensions["userAgent"] = userAgent.ToString();
            }
        });

        services.AddHealthChecks();

        services.AddControllersWithViews()
            .AddJsonOptions(options =>
            {
                if (!options.JsonSerializerOptions.Converters.Any(converter => converter is JsonStringEnumConverter))
                {
                    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                }
            });

        services.AddHttpClient();
        services.AddHttpContextAccessor();

        services.AddOptions<AllSkyCameraServiceOptions>()
            .BindConfiguration("AllSkyCamera")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<AllSkyImageSaveOptions>()
            .BindConfiguration("AllSkyImageSave")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<AllSkyImageCleanupOptions>()
            .BindConfiguration("AllSkyImageCleanup")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<AllSkyTimelapseOptions>()
            .BindConfiguration("AllSkyTimelapse")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IAllSkyCameraService, AllSkyCameraService>();
        services.AddSingleton<IAllSkyTimelapseService, AllSkyTimelapseService>();

        services.AddApiVersioning(options =>
        {
            options.AssumeDefaultVersionWhenUnspecified = true;
            options.DefaultApiVersion = new ApiVersion(1, 0);
            options.ReportApiVersions = true;
            options.ApiVersionReader = new UrlSegmentApiVersionReader();
        }).AddApiExplorer(options =>
        {
            options.GroupNameFormat = "'v'VVV";
            options.SubstituteApiVersionInUrl = true;
        });

        services.AddHostedService<AllSkyCameraServiceHost>();
        services.AddHostedService<AllSkyImageSaveService>();
        services.AddHostedService<AllSkyImageCleanupService>();
        services.AddHostedService<AllSkyTimelapseServiceHost>();
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

        if (!app.Environment.IsDevelopment())
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
        app.UseAuthorization();
        app.UseAntiforgery();
        app.MapStaticAssets();

        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = async (context, report) =>
            {
                context.Response.ContentType = "application/json";
                var response = new
                {
                    status = report.Status.ToString(),
                    checks = report.Entries.Select(entry => new
                    {
                        name = entry.Key,
                        status = entry.Value.Status.ToString(),
                        description = entry.Value.Description,
                        data = entry.Value.Data,
                        duration = entry.Value.Duration.ToString(),
                        exception = entry.Value.Exception?.Message,
                        tags = entry.Value.Tags
                    }),
                    totalDuration = report.TotalDuration.ToString()
                };
                await context.Response.WriteAsync(JsonSerializer.Serialize(response));
            }
        });

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("hardware")
        });

        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false
        });

        app.MapControllers();

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();
    }
}
