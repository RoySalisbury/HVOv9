using HVO.SkyMonitorV4.RPi.Components;
using HVO.SkyMonitorV4.RPi.HostedServices;
using HVO.SkyMonitorV4.RPi.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
        services.AddRazorComponents()
            .AddInteractiveServerComponents();

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

        services.AddHttpContextAccessor();

        services.AddHostedService<AllSkyCameraServiceHost>();
    }

    private static void Configure(WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        var enableHttpsRedirect = app.Configuration.GetValue("EnableHttpsRedirect", !app.Environment.IsDevelopment());
        if (enableHttpsRedirect)
        {
            app.UseHttpsRedirection();
        }

        app.UseRouting();
        app.UseAntiforgery();
        app.MapStaticAssets();

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();
    }
}
