using System;
using Microsoft.Extensions.Hosting;

namespace HVO.SkyMonitorV4.RPi.HostedServices;

public class AllSkyCameraServiceHost : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Your background task logic here
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}
