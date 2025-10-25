#nullable enable

using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HVO;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.SkyMonitorV4.RPi.HostedServices.AllSkyTimelapse;

public sealed class AllSkyTimelapseServiceHost : BackgroundService
{
    private readonly ILogger<AllSkyTimelapseServiceHost> _logger;
    private readonly AllSkyTimelapseOptions _options;
    private readonly IAllSkyTimelapseService _timelapseService;

    public AllSkyTimelapseServiceHost(
        ILogger<AllSkyTimelapseServiceHost> logger,
        IOptions<AllSkyTimelapseOptions> options,
        IAllSkyTimelapseService timelapseService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _timelapseService = timelapseService ?? throw new ArgumentNullException(nameof(timelapseService));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var serviceName = nameof(AllSkyTimelapseServiceHost);
        _logger.LogInformation("{BackgroundServiceName} is starting.", serviceName);
        using var registration = stoppingToken.Register(() => _logger.LogInformation("{BackgroundServiceName} is stopping.", serviceName));

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _timelapseService.RunScheduledAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "AllSky timelapse loop faulted. Restarting in {Delay}s.", _options.RestartOnFailureWaitTimeSeconds);
                    await Task.Delay(TimeSpan.FromSeconds(_options.RestartOnFailureWaitTimeSeconds), stoppingToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _logger.LogInformation("{BackgroundServiceName} has stopped.", serviceName);
        }
    }
}

public interface IAllSkyTimelapseService
{
    Task RunScheduledAsync(CancellationToken cancellationToken);

    Task<Result<string>> CreateTimelapseAsync(DateTimeOffset start, DateTimeOffset end, string? filePrefix = null, CancellationToken cancellationToken = default);
}

public sealed class AllSkyTimelapseService : IAllSkyTimelapseService
{
    private readonly ILogger<AllSkyTimelapseService> _logger;
    private readonly AllSkyTimelapseOptions _options;

    public AllSkyTimelapseService(
        ILogger<AllSkyTimelapseService> logger,
        IOptions<AllSkyTimelapseOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task RunScheduledAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.Now;
            var nextHour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Offset).AddHours(1);
            var delay = nextHour - now;

            if (delay > TimeSpan.Zero)
            {
                _logger.LogDebug("Next automatic timelapse scheduled for {NextRun} (in {Delay}).", nextHour, delay);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);

            var start = nextHour.AddHours(-1);
            var end = nextHour;

            var result = await CreateTimelapseAsync(start, end, _options.OutputPrefix, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                var error = result.Error;
                if (error is not null && error is not OperationCanceledException)
                {
                    _logger.LogDebug("Automatic timelapse generation completed with failure for interval {Start} - {End}: {Message}", start, end, error.Message);
                }
            }
        }
    }

    public async Task<Result<string>> CreateTimelapseAsync(DateTimeOffset start, DateTimeOffset end, string? filePrefix = null, CancellationToken cancellationToken = default)
    {
        var prefix = string.IsNullOrWhiteSpace(filePrefix) ? _options.OutputPrefix : filePrefix!;
        var baseDirectory = _options.ImageSaveRoot;

        if (!Directory.Exists(baseDirectory))
        {
            var exception = new DirectoryNotFoundException($"Image save root '{baseDirectory}' was not found.");
            _logger.LogWarning(exception, "Image save root {Root} was not found. Skipping timelapse generation.", baseDirectory);
            return Result<string>.Failure(exception);
        }

        var localStart = start.ToLocalTime();
        var localEnd = end.ToLocalTime().AddMilliseconds(-1);

        var hourDirectory = Path.Combine(
            baseDirectory,
            localStart.ToString("yyyy", CultureInfo.InvariantCulture),
            localStart.ToString("MM", CultureInfo.InvariantCulture),
            localStart.ToString("dd", CultureInfo.InvariantCulture),
            localStart.ToString("HH", CultureInfo.InvariantCulture));

        if (!Directory.Exists(hourDirectory))
        {
            var exception = new DirectoryNotFoundException($"Hour directory '{hourDirectory}' does not exist for interval {start:o} - {end:o}.");
            _logger.LogWarning(exception, "Hour directory {Directory} does not exist. Skipping timelapse interval {Start} - {End}.", hourDirectory, start, end);
            return Result<string>.Failure(exception);
        }

        var frames = Directory.EnumerateFiles(hourDirectory, "*.jpg", SearchOption.AllDirectories)
            .Select(path => new
            {
                Path = path,
                Ticks = ParseTicks(path)
            })
            .Where(x => x.Ticks.HasValue && x.Ticks.Value >= localStart.Ticks && x.Ticks.Value <= localEnd.Ticks)
            .OrderBy(x => x.Ticks)
            .Select(x => x.Path)
            .ToList();

        if (frames.Count < _options.FFMpegVideoFps)
        {
            var exception = new InvalidOperationException($"Not enough frames ({frames.Count}) to generate timelapse for interval {start:o} - {end:o}.");
            _logger.LogWarning(exception, "Not enough frames ({FrameCount}) to generate timelapse for interval {Start} - {End}.", frames.Count, start, end);
            return Result<string>.Failure(exception);
        }

        var listFilePath = Path.Combine(baseDirectory, $"{Guid.NewGuid():N}.txt");
        await WriteFfmpegFileListAsync(listFilePath, frames, cancellationToken).ConfigureAwait(false);

        var tempFileName = $"_{prefix}.{localStart:yyyyMMdd_HHmmss}.mp4";
        var tempOutputPath = Path.Combine(baseDirectory, tempFileName);
        var finalOutputPath = Path.Combine(baseDirectory, tempFileName.TrimStart('_'));

        if (File.Exists(tempOutputPath))
        {
            File.Delete(tempOutputPath);
        }

        if (File.Exists(finalOutputPath))
        {
            File.Delete(finalOutputPath);
        }

        try
        {
            var arguments = new StringBuilder()
                .Append("-r ")
                .Append(_options.FFMpegVideoFps.ToString(CultureInfo.InvariantCulture))
                .Append(" -f concat -safe 0 -i \"")
                .Append(listFilePath)
                .Append("\" ")
                .Append(_options.FFMpegOutputArgs)
                .Append(' ')
                .Append('"')
                .Append(tempOutputPath)
                .Append('"')
                .ToString();

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _options.FFMpegPath,
                    WorkingDirectory = baseDirectory,
                    Arguments = arguments,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    _logger.LogDebug("ffmpeg: {Line}", e.Data);
                }
            };

            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    _logger.LogInformation("ffmpeg: {Line}", e.Data);
                }
            };

            _logger.LogInformation("Starting timelapse generation. Command: {Command} {Arguments}", _options.FFMpegPath, arguments);

            process.Start();
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            if (process.ExitCode == 0)
            {
                File.Move(tempOutputPath, finalOutputPath, overwrite: true);
                _logger.LogInformation("Timelapse generated successfully at {OutputPath}.", finalOutputPath);
                return Result<string>.Success(finalOutputPath);
            }
            else
            {
                var exception = new InvalidOperationException($"ffmpeg exited with code {process.ExitCode} while generating timelapse for interval {start:o} - {end:o}.");
                _logger.LogError(exception, "ffmpeg exited with code {ExitCode} while generating timelapse for {Start} - {End}.", process.ExitCode, start, end);
                if (File.Exists(tempOutputPath))
                {
                    File.Delete(tempOutputPath);
                }
                return Result<string>.Failure(exception);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Timelapse generation failed for interval {Start} - {End}.", start, end);
            return Result<string>.Failure(ex);
        }
        finally
        {
            try
            {
                if (File.Exists(listFilePath))
                {
                    File.Delete(listFilePath);
                }
            }
            catch (Exception cleanupEx)
            {
                _logger.LogDebug(cleanupEx, "Failed to delete temporary ffmpeg list file {ListPath}.", listFilePath);
            }
        }

    }

    private static long? ParseTicks(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        return long.TryParse(fileName, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks) ? ticks : null;
    }

    private static async Task WriteFfmpegFileListAsync(string filePath, IReadOnlyCollection<string> frames, CancellationToken cancellationToken)
    {
        static string Escape(string path)
        {
            return path.Replace("'", "'\\''", StringComparison.Ordinal);
        }

        var lines = frames.Select(frame => $"file '{Escape(frame)}'");
        await File.WriteAllLinesAsync(filePath, lines, cancellationToken).ConfigureAwait(false);
    }
}
