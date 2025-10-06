using HVO.Playground.CLI.HostedServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HVO.Playground.CLI;

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

        builder.Services.AddHostedService<PlaygroundCliHostedService>();

        using var host = builder.Build();
        await host.RunAsync().ConfigureAwait(false);
    }
}
