using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using HVO.DataModels.Extensions;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Components.Web;
using HVO.WebSite.Playground.Middleware;
using Microsoft.AspNetCore.Http.Features;

namespace HVO.WebSite.Playground
{
    /// <summary>
    /// Entry point class for the HVO Website Playground application
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Application entry point
        /// </summary>
        /// <param name="args">Command line arguments</param>
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            // Add MVC and API services
            builder.Services.AddControllersWithViews();
            builder.Services.AddControllers();

            // Add ProblemDetails for standardized error responses
            builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Instance = $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}";
                context.ProblemDetails.Extensions.TryAdd("requestId", context.HttpContext.TraceIdentifier);

                var activity = context.HttpContext.Features.Get<IHttpActivityFeature>()?.Activity;
                context.ProblemDetails.Extensions.TryAdd("traceId", activity?.Id);
            });        

            builder.Services.AddExceptionHandler<HvoServiceExceptionHandler>();    

            // Add OpenAPI/Swagger services
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
                {
                    Title = "HVO Weather API",
                    Version = "v1.0",
                    Description = "Hualapai Valley Observatory Weather API for accessing current conditions and daily highs/lows",
                    Contact = new Microsoft.OpenApi.Models.OpenApiContact
                    {
                        Name = "HVO Development Team",
                        Email = "admin@hualapai-valley-observatory.com"
                    }
                });
                
                // Include XML comments for better documentation
                var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                if (File.Exists(xmlPath))
                {
                    c.IncludeXmlComments(xmlPath);
                }
            });

            // Add HVO Data Services with Entity Framework
            builder.Services.AddHvoDataServices(builder.Configuration);

            // Add application services
            builder.Services.AddScoped<HVO.WebSite.Playground.Services.IWeatherService, HVO.WebSite.Playground.Services.WeatherService>();

            // Configure HttpClient for Blazor Server components
            builder.Services.AddHttpClient("LocalApi", client =>
            {
                client.BaseAddress = new Uri("http://localhost:5136");
            });

            builder.Services.AddHttpContextAccessor();

            // Add API versioning
            builder.Services.AddApiVersioning(opt =>
            {
                opt.DefaultApiVersion = new ApiVersion(1, 0);
                opt.AssumeDefaultVersionWhenUnspecified = true;
                opt.ApiVersionReader = new UrlSegmentApiVersionReader();
            })
            .AddMvc();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            app.UseExceptionHandler();

            if (!app.Environment.IsDevelopment())
            {
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }
            else
            {
                // Enable Swagger in development environment
                app.UseSwagger();
                app.UseSwaggerUI(c =>
                {
                    c.SwaggerEndpoint("/swagger/v1/swagger.json", "HVO Weather API v1.0");
                    c.RoutePrefix = "swagger"; // Set Swagger UI at /swagger
                });
            }

            app.UseHttpsRedirection();

            app.UseRouting();
            app.UseAntiforgery();

            app.MapStaticAssets();

            // Map MVC controllers
            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            // Map API controllers
            app.MapControllers();

            app.MapRazorComponents<Components.App>()
                .AddInteractiveServerRenderMode();

            app.Run();
        }
    }
}
