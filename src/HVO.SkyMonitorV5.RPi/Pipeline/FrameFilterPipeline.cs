#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Pipeline.Filters;
using HVO.SkyMonitorV5.RPi.Skia;
using HVO.SkyMonitorV5.RPi.Telemetry;
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
        private readonly SkiaSurfacePool _surfacePool;
        private readonly FilterTelemetryStore _telemetryStore = new();
        private readonly ISkyMonitorTelemetryRecorder? _telemetryRecorder;

        public FrameFilterPipeline(
            IEnumerable<IFrameFilter> filters,
            SkiaSurfacePool surfacePool,
            ILogger<FrameFilterPipeline> logger,
            ISkyMonitorTelemetryRecorder? telemetryRecorder = null)
        {
            _filters = filters ?? throw new ArgumentNullException(nameof(filters));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _surfacePool = surfacePool ?? throw new ArgumentNullException(nameof(surfacePool));
            _telemetryRecorder = telemetryRecorder;
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

            FilterFrame? filterFrame = null;
            SKBitmap? legacyBitmap = null;
            SKImage? disposableSourceImage = null;

            try
            {
                var pipelineStopwatch = Stopwatch.StartNew();

                var sourceImage = stackResult.StackedImmutableImage;
                if (sourceImage is null)
                {
                    disposableSourceImage = SkiaImageUtilities.SnapshotToImmutable(null, stackResult.StackedImage)
                        ?? throw new InvalidOperationException("Unable to snapshot stacked image for filter processing.");
                    sourceImage = disposableSourceImage;
                }

                var surfaceStopwatch = Stopwatch.StartNew();
                var surfaceLease = _surfacePool.RentLinearSurface(sourceImage.Width, sourceImage.Height);
                var surface = surfaceLease.Surface;
                surface.Canvas.Clear(SKColors.Transparent);
                surface.Canvas.DrawImage(sourceImage, 0, 0);
                surface.Canvas.Flush();
                surfaceStopwatch.Stop();

                filterFrame = new FilterFrame(surfaceLease);
                var renderContext = frameContext is not null ? new FrameRenderContext(frameContext) : null;

                var appliedFilters = new List<string>();
                List<FilterTiming>? filterTimings = null;
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    filterTimings = new List<FilterTiming>();
                }

                var legacyBitmapStale = true;

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

                        if (filter is IImageFrameFilter imageFilter)
                        {
                            await imageFilter.ApplyAsync(filterFrame, stackResult, configuration, renderContext, cancellationToken).ConfigureAwait(false);
                            legacyBitmapStale = true;
                        }
                        else
                        {
                            if (legacyBitmap is null || legacyBitmapStale)
                            {
                                legacyBitmap?.Dispose();
                                legacyBitmap = filterFrame.CreateBitmapView();
                                legacyBitmapStale = false;
                            }

                            await filter.ApplyAsync(legacyBitmap, stackResult, configuration, renderContext, cancellationToken).ConfigureAwait(false);
                            filterFrame.BlitBitmap(legacyBitmap);
                        }

                        filterStopwatch.Stop();
                        var duration = filterStopwatch.Elapsed.TotalMilliseconds;

                        if (filterTimings is not null)
                        {
                            filterTimings.Add(new FilterTiming(filter.Name, duration));
                        }

                        var telemetrySnapshot = _telemetryStore.Record(filter.Name, duration);
                        if (telemetrySnapshot is not null)
                        {
                            _telemetryRecorder?.RecordFilterMetricSample(
                                telemetrySnapshot.FilterName,
                                telemetrySnapshot.AppliedCount,
                                telemetrySnapshot.LastDurationMilliseconds,
                                telemetrySnapshot.AverageDurationMilliseconds);
                        }
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
                var encodingSettings = configuration.ProcessedImageEncoding ?? new ImageEncodingSettings();
                var skiaFormat = ToSkiaFormat(encodingSettings.Format);
                var quality = Math.Clamp(encodingSettings.Quality, 1, 100);

                var processedImage = filterFrame.SnapshotImage();
                byte[] bytes;

                try
                {
                    using var data = processedImage.Encode(skiaFormat, quality);
                    if (data is null)
                    {
                        throw new InvalidOperationException($"Failed to encode processed frame using format {skiaFormat}.");
                    }

                    bytes = data.ToArray();
                }
                finally
                {
                    // filterFrame resources are released below in finally block
                }

                encodeStopwatch.Stop();

                pipelineStopwatch.Stop();

                if (filterTimings is not null)
                {
                    var filterBreakdown = filterTimings.Count == 0
                        ? "none"
                        : string.Join(", ", filterTimings.Select(t => $"{t.Filter}:{t.DurationMs:F1}ms"));

                    _logger.LogDebug(
                        "Filter pipeline completed in {TotalMs}ms (surface {SurfaceMs}ms, encode {EncodeMs}ms). Filters: {Breakdown}.",
                        pipelineStopwatch.Elapsed.TotalMilliseconds,
                        surfaceStopwatch.Elapsed.TotalMilliseconds,
                        encodeStopwatch.Elapsed.TotalMilliseconds,
                        filterBreakdown);
                }

                return new ProcessedFrame(
                    stackResult.FrameId,
                    stackResult.Timestamp,
                    stackResult.Exposure,
                    bytes,
                    ToContentType(encodingSettings.Format),
                    stackResult.FramesStacked,
                    stackResult.IntegrationMilliseconds,
                    appliedFilters,
                    ProcessingMilliseconds: 0,
                    ImmutableImage: processedImage);
            }
            finally
            {
                filterFrame?.Dispose();
                legacyBitmap?.Dispose();
                disposableSourceImage?.Dispose();
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