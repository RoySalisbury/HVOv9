using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using HVO.WebSite.SkyMonitorV4.Components;

namespace HVO.WebSite.SkyMonitorV4;

public class Program
{
    public static void Main(string[] args)
    {
        // Log SDK version early for diagnostics
        // try
        // {
        //     var ver = ASICameraDll.GetSDKVersion();
        //     if (!string.IsNullOrWhiteSpace(ver))
        //     {
        //         Console.WriteLine($"ZWO ASI SDK Version: {ver}");
        //     }
        // }
        // catch { }

        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        app.UseHttpsRedirection();

        app.UseAntiforgery();

        app.MapStaticAssets();
        app.MapRazorComponents<HVO.WebSite.SkyMonitorV4.Components.App>()
            .AddInteractiveServerRenderMode();

        app.Run();
    }


}
