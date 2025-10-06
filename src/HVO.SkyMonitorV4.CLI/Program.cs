using HVO.SkyMonitorV4.CLI.HostedServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV4.CLI;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
        });

        builder.Services.AddHostedService<CliRunnerHostedService>();

        using var host = builder.Build();
        await host.RunAsync().ConfigureAwait(false);
    }
}


