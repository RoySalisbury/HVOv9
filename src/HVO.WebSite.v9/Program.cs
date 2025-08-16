using HVO.DataModels.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Text.Json.Serialization;

namespace HVO.WebSite.v9
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            ConfigureServices(builder.Services, builder.Configuration, builder.Environment);

            var app = builder.Build();
            Configure(app, app.Environment);

            app.Run();
        }

        private static void ConfigureServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
        {
            services.AddRazorComponents().AddInteractiveServerComponents();
            services.AddControllersWithViews()
                .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

            // Data access via HVO.DataModels (HvoDbContext)
            services.AddHvoDataServices(configuration);

            // Optional: ProblemDetails for consistent errors (can be enabled later)
            // services.AddProblemDetails();
        }

        private static void Configure(WebApplication app, IHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseStaticFiles();
            app.UseRouting();

            // HTTPS redirection based on configuration
            var enableHttpsRedirect = app.Configuration.GetValue("EnableHttpsRedirect", !env.IsDevelopment());
            if (enableHttpsRedirect)
            {
                app.UseHttpsRedirection();
            }

            app.MapControllers();

            // Minimal Blazor host
            app.MapRazorComponents<Components.App>()
               .AddInteractiveServerRenderMode();

            // Health endpoint (basic)
            app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }));
        }
    }
}
