using System.Text.Json.Serialization;
using Asp.Versioning;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Scalar.AspNetCore;
using HVO.WebSite.RoofControllerV4.Logic;
using HVO.WebSite.RoofControllerV4.Models;
using HVO.WebSite.RoofControllerV4.HostedServices;
using HVO.WebSite.RoofControllerV4.Middleware;
using HVO.WebSite.RoofControllerV4.HealthChecks;
using HVO.Iot.Devices.Abstractions;
using HVO.Iot.Devices.Implementation;

using System.Runtime.Loader;
using HVO.Iot.Devices.Iot.Devices.Sequent;

namespace HVO.WebSite.RoofControllerV4;

public class Program
{
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            try { Console.WriteLine("AppDomain.CurrentDomain.UnhandledException"); }
            catch { /* swallow */ }
        };

        AppDomain.CurrentDomain.ProcessExit += (s, e) =>
        {
            try { Console.WriteLine("AppDomain.CurrentDomain.ProcessExit"); }
            catch { /* swallow */ }
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            try { Console.WriteLine("TaskScheduler.UnobservedTaskException"); }
            catch { /* swallow */ }
        };

        AssemblyLoadContext.Default.Unloading += _ =>
        {
            try { Console.WriteLine("AssemblyLoadContext.Default.Unloading"); }
            catch { /* swallow */ }
        };



        var builder = WebApplication.CreateBuilder(args);
        ConfigureServices(builder.Services, builder.Configuration, builder.Environment);

        var app = builder.Build();

        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Register(() => Console.WriteLine("IHostApplicationLifetime.Register"));        

        Configure(app);

        app.Run();
    }


    private static void ConfigureServices(IServiceCollection services, ConfigurationManager Configuration, IWebHostEnvironment Environment)
    {
        // ============================================================================
        // ASP.NET CORE BUILT-IN FUNCTIONALITY GUIDELINES
        // ============================================================================
        // Always use built-in ASP.NET Core endpoints and middleware instead of 
        // creating custom controllers for standard functionality:
        //
        // ✅ Health Checks: Use MapHealthChecks() - NOT custom HealthController
        // ✅ OpenAPI/Swagger: Use AddOpenApi() - NOT custom documentation endpoints  
        // ✅ Exception Handling: Use AddExceptionHandler() - NOT custom error controllers
        // ✅ Problem Details: Use AddProblemDetails() - NOT custom error responses
        // ✅ Static Files: Use UseStaticFiles() - NOT custom file serving controllers
        // ✅ CORS: Use AddCors() - NOT custom CORS controllers
        // ✅ Authentication: Use AddAuthentication() - NOT custom auth controllers
        // ✅ Authorization: Use AddAuthorization() - NOT custom authz controllers
        // ✅ Rate Limiting: Use AddRateLimiter() - NOT custom rate limiting controllers
        // ✅ Caching: Use AddResponseCaching() - NOT custom cache controllers
        // ✅ API Versioning: Use AddApiVersioning() - NOT custom version controllers
        // ============================================================================

        services.AddOptions();
        services.Configure<RoofControllerOptionsV4>(Configuration.GetSection(nameof(RoofControllerOptionsV4)));
        services.Configure<RoofControllerHostOptionsV4>(Configuration.GetSection(nameof(RoofControllerHostOptionsV4)));

        // Add Razor Components for Blazor Server
        services.AddRazorComponents()
            .AddInteractiveServerComponents();

        services.AddSingleton<FourRelayFourInputHat>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<FourRelayFourInputHat>>();
            return new FourRelayFourInputHat(logger: logger);
        });


        services.AddHostedService<RoofControllerServiceV4Host>();

        // Register RoofController based on configuration
        services.AddSingleton<IRoofControllerServiceV4, RoofControllerServiceV4>();

        // Add exception handling middleware
        // NOTE: Use built-in exception handling instead of custom error controllers
        // This provides consistent error responses and integrates with Problem Details
        services.AddExceptionHandler<HvoServiceExceptionHandler>();

        // Add health checks
        // NOTE: Use built-in ASP.NET Core health check endpoints instead of creating custom controllers
        // The MapHealthChecks middleware below provides all necessary endpoints:
        // - /health (detailed health information)
        // - /health/ready (readiness probes for load balancers)  
        // - /health/live (liveness probes for container orchestration)
        // Do NOT create duplicate HealthController - use the built-in functionality
        services.AddHealthChecks()
            .AddCheck<RoofControllerHealthCheck>("roof_controller", tags: ["roof", "hardware"]);

        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
        // NOTE: Use built-in OpenAPI/Swagger functionality instead of custom documentation endpoints
        // This provides automatic API documentation generation from controller attributes
        services.AddOpenApi("v4");

        services.AddApiVersioning(setup =>
        {
            setup.DefaultApiVersion = new ApiVersion(4, 0);
            setup.AssumeDefaultVersionWhenUnspecified = true;
            setup.ReportApiVersions = true;
            setup.ApiVersionReader = new UrlSegmentApiVersionReader(); // ApiVersionReader.Combine(new QueryStringApiVersionReader("version"), new HeaderApiVersionReader("api-version"), new MediaTypeApiVersionReader("version")); 
        }).AddApiExplorer(options =>
        {
            options.GroupNameFormat = "'v'VVV";
            options.SubstituteApiVersionInUrl = true;
        });

        // Configure Problem Details for consistent error responses
        services.AddProblemDetails(configure =>
        {
            configure.CustomizeProblemDetails = context =>
            {
                // Add common properties to all problem details
                context.ProblemDetails.Instance = $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}";
                context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
                context.ProblemDetails.Extensions["timestamp"] = DateTime.UtcNow;

                // Add request information for debugging
                if (context.HttpContext.Request.Headers.ContainsKey("User-Agent"))
                {
                    context.ProblemDetails.Extensions["userAgent"] = context.HttpContext.Request.Headers["User-Agent"].ToString();
                }
            };
        });

        // Enable endpoints API explorer for OpenAPI
        services.AddEndpointsApiExplorer();

        // Add MVC + Views + JSON enum string serialization (single registration to avoid overriding options)
        services.AddControllersWithViews()
            .AddJsonOptions(options =>
            {
                if (!options.JsonSerializerOptions.Converters.Any(c => c is JsonStringEnumConverter))
                {
                    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                }
            });

        // Add HttpClient for API calls
        services.AddHttpClient();

        // Add HttpContextAccessor for Blazor components
        services.AddHttpContextAccessor();
    }

    private static void Configure(WebApplication app)
    {
        // ============================================================================
        // MIDDLEWARE PIPELINE CONFIGURATION
        // ============================================================================
        // Use built-in ASP.NET Core middleware in the correct order:
        // 1. Exception handling (UseExceptionHandler)
        // 2. Status code pages (UseStatusCodePages) 
        // 3. HTTPS redirection (UseHttpsRedirection)
        // 4. Authentication (UseAuthentication)
        // 5. Authorization (UseAuthorization)
        // 6. Endpoint mapping (MapControllers, MapHealthChecks, etc.)
        // ============================================================================

        // Add exception handling middleware
        app.UseExceptionHandler();

        // Add Problem Details middleware for consistent error responses
        app.UseStatusCodePages();

        // Built-in OpenAPI endpoint - provides automatic API documentation
        // Available at: /openapi/v4.json
        app.MapOpenApi();

        if (app.Environment.IsDevelopment())
        {
            // Built-in interactive API documentation - provides Scalar UI
            // Available at: /scalar/v1 (interactive API explorer)
            app.MapScalarApiReference();
            app.UseDeveloperExceptionPage();
        }

        // In Development, disable HTTPS redirection to avoid cert prompts and warnings
        if (!app.Environment.IsDevelopment())
        {
            app.UseHttpsRedirection();
        }
        
        // Serve static web assets (including the generated .styles.css bundle)
        app.UseStaticFiles();
        app.UseRouting();
        app.UseAntiforgery();

        app.UseAuthorization();

        // Add health check endpoints
        // IMPORTANT: These are the RECOMMENDED ASP.NET Core health check endpoints
        // Do NOT duplicate these with custom controllers - use these built-in endpoints:

        // Detailed health endpoint with comprehensive information
        // Use this for: monitoring dashboards, detailed health reporting, troubleshooting
        app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            ResponseWriter = async (context, report) =>
            {
                context.Response.ContentType = "application/json";
                var response = new
                {
                    status = report.Status.ToString(),
                    checks = report.Entries.Select(x => new
                    {
                        name = x.Key,
                        status = x.Value.Status.ToString(),
                        description = x.Value.Description,
                        data = x.Value.Data,
                        duration = x.Value.Duration.ToString(),
                        exception = x.Value.Exception?.Message,
                        tags = x.Value.Tags
                    }),
                    totalDuration = report.TotalDuration.ToString()
                };
                await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(response));
            }
        });

        // Readiness probe endpoint for load balancers and orchestration
        // Use this for: Kubernetes readiness probes, load balancer health checks
        // Only checks hardware-tagged components to determine if service is ready to serve traffic
        app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("hardware")
        });

        // Liveness probe endpoint for container orchestration
        // Use this for: Kubernetes liveness probes, container restart decisions
        // Always returns healthy if the application is running (no specific checks)
        app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = _ => false // Always returns healthy for liveness
        });

        // Map Razor components for Blazor Server
        app.MapRazorComponents<Components.App>()
            .AddInteractiveServerRenderMode();

        // ============================================================================
        // COMMENTED OUT CODE - Examples of other built-in ASP.NET Core functionality
        // ============================================================================
        // Uncomment and configure as needed for your application:

        // Built-in Swagger UI (alternative to Scalar):
        // app.UseSwagger();
        // app.UseSwaggerUI(options =>
        // {
        //     var descriptions = app.DescribeApiVersions();
        //     foreach (var description in descriptions)
        //     {
        //         options.SwaggerEndpoint($"/swagger/{description.GroupName}/swagger.json", description.GroupName.ToUpperInvariant());
        //     }
        // });

        // Built-in static file serving (for wwwroot folder):
        // app.UseStaticFiles();

        // Built-in anti-forgery token support:
        // app.UseAntiforgery();

        app.MapControllers();
    }
}
