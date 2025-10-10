#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Pipeline.Filters;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Pipeline
{
    /// <summary>
    /// Orchestrates running all registered IFrameFilter instances, in order,
    /// against the current stacked frame. Produces a processed frame image
    /// that can be distributed to downstream consumers.
    /// </summary>
    public sealed class FrameFilterPipeline : IFrameFilterPipeline
    {
        private readonly IEnumerable<IFrameFilter> _filters;
        private readonly ILogger<FrameFilterPipeline> _logger;
        private readonly FilterTelemetryStore _telemetryStore = new();

        public FrameFilterPipeline(IEnumerable<IFrameFilter> filters, ILogger<FrameFilterPipeline> logger)
        {
            _filters = filters ?? throw new ArgumentNullException(nameof(filters));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Runs all applicable filters. Filters draw directly into the SKBitmap.
        /// </summary>
        public async Task<ProcessedFrame> ProcessAsync(
            FrameStackResult stackResult,
            CameraConfiguration configuration,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var frameContext = stackResult.Context;

            try
            {
                var pipelineStopwatch = Stopwatch.StartNew();
                var copyStopwatch = Stopwatch.StartNew();
                var copy = stackResult.StackedImage.Copy();
                copyStopwatch.Stop();
                if (copy is null)
                {
                    throw new InvalidOperationException("Unable to copy raw frame image for filter processing.");
                }

                using var bitmap = copy;
                var renderContext = frameContext is not null ? new FrameRenderContext(frameContext) : null;

                var appliedFilters = new List<string>();
                List<FilterTiming>? filterTimings = null;
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    filterTimings = new List<FilterTiming>();
                }

                foreach (var filter in _filters)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!IsFilterEnabled(configuration, filter.Name))
                    {
                        continue;
                    }

                    bool apply;
                    try
                    {
                        apply = filter.ShouldApply(configuration);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Filter {Filter} ShouldApply() threw.", filter.Name);
                        continue;
                    }

                    if (!apply)
                    {
                        continue;
                    }

                    var filterStopwatch = Stopwatch.StartNew();

                    try
                    {
                        appliedFilters.Add(filter.Name);
                        await filter.ApplyAsync(bitmap, stackResult, configuration, renderContext, cancellationToken).ConfigureAwait(false);

                        filterStopwatch.Stop();
                        var duration = filterStopwatch.Elapsed.TotalMilliseconds;

                        if (filterTimings is not null)
                        {
                            filterTimings.Add(new FilterTiming(filter.Name, duration));
                        }

                        _telemetryStore.Record(filter.Name, duration);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Filter {Filter} ApplyAsync() failed; continuing.", filter.Name);
                    }
                }

                var encodeStopwatch = Stopwatch.StartNew();
                // Encode the updated bitmap into the processed frame payload
                using var image = SKImage.FromBitmap(bitmap);
                var encodingSettings = configuration.ProcessedImageEncoding ?? new ImageEncodingSettings();
                var skiaFormat = ToSkiaFormat(encodingSettings.Format);
                var quality = Math.Clamp(encodingSettings.Quality, 1, 100);
                using var data = image.Encode(skiaFormat, quality);
                var bytes = data.ToArray();
                encodeStopwatch.Stop();

                pipelineStopwatch.Stop();

                if (filterTimings is not null)
                {
                    var filterBreakdown = filterTimings.Count == 0
                        ? "none"
                        : string.Join(", ", filterTimings.Select(t => $"{t.Filter}:{t.DurationMs:F1}ms"));

                    _logger.LogDebug(
                        "Filter pipeline completed in {TotalMs}ms (copy {CopyMs}ms, encode {EncodeMs}ms). Filters: {Breakdown}.",
                        pipelineStopwatch.Elapsed.TotalMilliseconds,
                        copyStopwatch.Elapsed.TotalMilliseconds,
                        encodeStopwatch.Elapsed.TotalMilliseconds,
                        filterBreakdown);
                }

                return new ProcessedFrame(
                    stackResult.Timestamp,
                    stackResult.Exposure,
                    bytes,
                    ToContentType(encodingSettings.Format),
                    stackResult.FramesStacked,
                    stackResult.IntegrationMilliseconds,
                    appliedFilters,
                    ProcessingMilliseconds: 0);
            }
            finally
            {
                frameContext?.Dispose();
            }
        }

        public FilterMetricsSnapshot GetMetricsSnapshot() => _telemetryStore.Snapshot();

        private static bool IsFilterEnabled(CameraConfiguration configuration, string filterName)
        {
            var filters = configuration.FrameFilters;
            if (filters is null || filters.Count == 0)
            {
                return true;
            }

            for (var i = 0; i < filters.Count; i++)
            {
                if (string.Equals(filters[i], filterName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        // FrameStackResult exposes the raw stacked SKBitmap; filters operate on a copy to avoid mutating shared state.

        private static SKEncodedImageFormat ToSkiaFormat(ImageEncodingFormat format) => format switch
        {
            ImageEncodingFormat.Jpeg => SKEncodedImageFormat.Jpeg,
            ImageEncodingFormat.Png => SKEncodedImageFormat.Png,
            _ => SKEncodedImageFormat.Png
        };

        private static string ToContentType(ImageEncodingFormat format) => format switch
        {
            ImageEncodingFormat.Jpeg => "image/jpeg",
            ImageEncodingFormat.Png => "image/png",
            _ => "application/octet-stream"
        };
    }

    internal readonly record struct FilterTiming(string Filter, double DurationMs);
}