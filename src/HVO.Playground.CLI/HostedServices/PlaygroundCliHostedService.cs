#nullable enable

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HVO.Playground.CLI.HostedServices;

public sealed class PlaygroundCliHostedService : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan InputPollInterval = TimeSpan.FromMilliseconds(125);

    private readonly ILogger<PlaygroundCliHostedService> _logger;
    private readonly IHostApplicationLifetime _lifetime;

    private double _lastCpuTotalMilliseconds;
    private DateTimeOffset _lastCpuSampleTimestamp;

    public PlaygroundCliHostedService(ILogger<PlaygroundCliHostedService> logger, IHostApplicationLifetime lifetime)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var serviceName = nameof(PlaygroundCliHostedService);
        using var registration = stoppingToken.Register(() =>
            _logger.LogInformation("{ServiceName} received cancellation request.", serviceName));

        _logger.LogInformation("{ServiceName} starting.", serviceName);

        try
        {
            await RunAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected during shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in {ServiceName}.", serviceName);
            throw;
        }
        finally
        {
            _logger.LogInformation("{ServiceName} stopped.", serviceName);
        }
    }

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var uiToken = linkedCts.Token;

        var layout = CreateLayout();

        var inputTask = Task.Run(() => MonitorInputAsync(linkedCts, uiToken), CancellationToken.None);

        await AnsiConsole.Live(layout)
            .AutoClear(false)
            .StartAsync(async ctx =>
        {
            while (!uiToken.IsCancellationRequested)
            {
                var metrics = CaptureSystemMetrics();
                var processes = CaptureProcessSnapshots();

                UpdateLayout(layout, metrics, processes);
                ctx.Refresh();

                try
                {
                    await Task.Delay(RefreshInterval, uiToken).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }).ConfigureAwait(false);

        linkedCts.Cancel();

        try
        {
            await inputTask.ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            // Ignore cancellation triggered by linked token source.
        }

        if (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("User requested shutdown. Stopping application host.");
            _lifetime.StopApplication();
        }
    }

    private static Layout CreateLayout()
    {
        var layout = new Layout("Root")
            .SplitRows(
                new Layout("Header").Size(6),
                new Layout("Body"),
                new Layout("Footer").Size(3));

        var headerContent = new Rows(
            new FigletText("HVO Playground").Color(Color.Cyan1),
            new Markup("[gray]Interactive diagnostics inspired by htop[/]"));

        layout["Header"].Update(new Panel(headerContent)
            .Border(BoxBorder.Rounded)
            .BorderStyle(Style.Parse("deepskyblue3"))
            .Expand());

        layout["Footer"].Update(new Panel(new Markup("Press [green]Q[/] to exit."))
            .Border(BoxBorder.Rounded)
            .BorderStyle(Style.Parse("yellow"))
            .Expand());

        layout["Body"].Update(new Panel(new Markup("Loading metrics..."))
            .Border(BoxBorder.Rounded)
            .Expand());

        return layout;
    }

    private void UpdateLayout(Layout layout, SystemMetrics metrics, IReadOnlyList<ProcessSnapshot> processes)
    {
        var metricsTable = BuildMetricsTable(metrics);
        var processTable = BuildProcessTable(processes);
        var gcTable = BuildGcTable();

        var grid = new Grid().Expand();
        grid.AddColumn(new GridColumn().NoWrap());
        grid.AddColumn(new GridColumn());
        grid.AddRow(metricsTable, processTable);
        grid.AddRow(gcTable, new Panel(new Markup($"[gray]Updated at {metrics.SampleTime:HH:mm:ss} UTC[/]"))
            .Border(BoxBorder.Square)
            .Expand());

        layout["Body"].Update(grid);
    }

    private Table BuildMetricsTable(SystemMetrics metrics)
    {
        var table = new Table().Border(TableBorder.Rounded).Title("[bold cyan]Process Metrics[/]").Expand();
        table.AddColumn(new TableColumn("Metric").Centered());
        table.AddColumn(new TableColumn("Value"));

        table.AddRow("CPU", $"{metrics.CpuPercent:F1}%");
        table.AddRow("Working Set", $"{metrics.WorkingSetMb:F1} MB");
        table.AddRow("Peak Working Set", $"{metrics.PeakWorkingSetMb:F1} MB");
        table.AddRow("Threads", metrics.ThreadCount.ToString("N0"));
        table.AddRow("Handles", metrics.HandleCount.ToString("N0"));

        return table;
    }

    private Table BuildProcessTable(IReadOnlyList<ProcessSnapshot> processes)
    {
        var table = new Table().Border(TableBorder.Rounded).Title("[bold green]Top Processes[/]").Expand();
        table.AddColumn("PID");
        table.AddColumn(new TableColumn("Name").NoWrap());
        table.AddColumn("Working Set (MB)");

        if (processes.Count == 0)
        {
            table.AddRow("-", "(no access)", "-");
            return table;
        }

        foreach (var process in processes)
        {
            table.AddRow(
                process.Id.ToString(),
                process.Name,
                process.WorkingSetMb.ToString("F1"));
        }

        return table;
    }

    private static Table BuildGcTable()
    {
        var table = new Table().Border(TableBorder.Rounded).Title("[bold yellow]GC Collections[/]").Expand();
        table.AddColumn("Generation");
        table.AddColumn("Collections");

        for (var generation = 0; generation <= GC.MaxGeneration; generation++)
        {
            table.AddRow(generation.ToString(), GC.CollectionCount(generation).ToString("N0"));
        }

        return table;
    }

    private SystemMetrics CaptureSystemMetrics()
    {
        var process = Process.GetCurrentProcess();
        var sampleTime = DateTimeOffset.UtcNow;
        var totalCpu = process.TotalProcessorTime.TotalMilliseconds;

        double cpuPercent = 0;

        if (_lastCpuSampleTimestamp != DateTimeOffset.MinValue)
        {
            var cpuDelta = totalCpu - _lastCpuTotalMilliseconds;
            var wallClockDelta = (sampleTime - _lastCpuSampleTimestamp).TotalMilliseconds * Environment.ProcessorCount;

            if (wallClockDelta > 0)
            {
                cpuPercent = Math.Clamp(cpuDelta / wallClockDelta * 100d, 0d, 100d);
            }
        }

        _lastCpuTotalMilliseconds = totalCpu;
        _lastCpuSampleTimestamp = sampleTime;

        return new SystemMetrics(
            cpuPercent,
            process.WorkingSet64 / (1024d * 1024d),
            process.PeakWorkingSet64 / (1024d * 1024d),
            process.Threads.Count,
            process.HandleCount,
            sampleTime);
    }

    private IReadOnlyList<ProcessSnapshot> CaptureProcessSnapshots()
    {
        try
        {
            return Process.GetProcesses()
                .Select(process =>
                {
                    try
                    {
                        return new ProcessSnapshot(
                            process.ProcessName,
                            process.Id,
                            process.WorkingSet64 / (1024d * 1024d));
                    }
                    catch
                    {
                        return null;
                    }
                })
                .Where(snapshot => snapshot is not null)
                .Cast<ProcessSnapshot>()
                .OrderByDescending(snapshot => snapshot.WorkingSetMb)
                .Take(8)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to enumerate processes.");
            return Array.Empty<ProcessSnapshot>();
        }
    }

    private async Task MonitorInputAsync(CancellationTokenSource cancelSource, CancellationToken token)
    {
        if (Console.IsInputRedirected)
        {
            // No interactive input available; exit when host stops.
            await Task.Delay(Timeout.Infinite, token).ContinueWith(_ => { }, TaskContinuationOptions.ExecuteSynchronously).ConfigureAwait(false);
            return;
        }

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true);
                    if (key.Key is ConsoleKey.Q or ConsoleKey.Escape)
                    {
                        _logger.LogInformation("Exit command received.");
                        cancelSource.Cancel();
                        break;
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // Console input might not be readable in all environments.
                break;
            }

            try
            {
                await Task.Delay(InputPollInterval, token).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private sealed record SystemMetrics(
        double CpuPercent,
        double WorkingSetMb,
        double PeakWorkingSetMb,
        int ThreadCount,
        int HandleCount,
        DateTimeOffset SampleTime);

    private sealed record ProcessSnapshot(
        string Name,
        int Id,
        double WorkingSetMb);
}
