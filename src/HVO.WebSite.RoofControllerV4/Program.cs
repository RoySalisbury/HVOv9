using System.Text.Json.Serialization;
using Asp.Versioning;
using Microsoft.OpenApi.Models;
using Scalar.AspNetCore;
using HVO.WebSite.RoofControllerV4.Logic;
using HVO.WebSite.RoofControllerV4.HostedServices;
using HVO.WebSite.RoofControllerV4.Middleware;

namespace HVO.WebSite.RoofControllerV4;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        ConfigureServices(builder.Services, builder.Configuration);

        var app = builder.Build();
        Configure(app);

        app.Run();
    }


    private static void ConfigureServices(IServiceCollection services, ConfigurationManager Configuration)
    {
        services.AddOptions();
        services.Configure<RoofControllerOptions>(Configuration.GetSection(nameof(RoofControllerOptions)));
        services.Configure<RoofControllerHostOptions>(Configuration.GetSection(nameof(RoofControllerHostOptions)));

        services.AddSingleton<System.Device.Gpio.GpioController>();
        services.AddSingleton<IRoofController, RoofController>();
        services.AddHostedService<RoofControllerHost>();

        // Add exception handling middleware
        services.AddExceptionHandler<HvoServiceExceptionHandler>();

        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
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


        services.AddProblemDetails(configure =>
        {
        });

        //services.AddEndpointsApiExplorer();

        services.AddControllers()
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
    }

    private static void Configure(WebApplication app)
    {
        // Add exception handling middleware
        app.UseExceptionHandler();

        app.MapOpenApi();

        if (app.Environment.IsDevelopment())
        {
            app.MapScalarApiReference();
            app.UseDeveloperExceptionPage();
        }

        app.UseHttpsRedirection();

        app.UseAuthorization();

        app.MapControllers();

        // app.UseSwagger();
        // app.UseSwaggerUI(options =>
        // {
        //     var descriptions = app.DescribeApiVersions();
        //     foreach (var description in descriptions)
        //     {
        //         options.SwaggerEndpoint($"/swagger/{description.GroupName}/swagger.json", description.GroupName.ToUpperInvariant());
        //     }
        // });

        // app.UseStaticFiles();
        // app.UseAntiforgery();

        app.MapControllers();
    }
}
