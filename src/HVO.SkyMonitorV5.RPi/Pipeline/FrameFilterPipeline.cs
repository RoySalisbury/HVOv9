#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Pipeline.Composition;
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
        private readonly FrameComposer _frameComposer;
        private readonly ILogger<FrameFilterPipeline> _logger;
        private readonly FilterTelemetryStore _telemetryStore = new();
        private readonly ISkyMonitorTelemetryRecorder? _telemetryRecorder;

        public FrameFilterPipeline(
            IEnumerable<IFrameFilter> filters,
            FrameComposer frameComposer,
            ILogger<FrameFilterPipeline> logger,
            ISkyMonitorTelemetryRecorder? telemetryRecorder = null)
        {
            _filters = filters ?? throw new ArgumentNullException(nameof(filters));
            _frameComposer = frameComposer ?? throw new ArgumentNullException(nameof(frameComposer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

            SKImage? disposableSourceImage = null;
            SKImage? compositionImage = null;

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

                var renderContext = frameContext is not null ? new FrameRenderContext(frameContext) : null;
                var composition = await _frameComposer.ComposeAsync(
                    sourceImage,
                    stackResult,
                    configuration,
                    renderContext,
                    _filters,
                    cancellationToken).ConfigureAwait(false);

                compositionImage = composition.Image;

                foreach (var execution in composition.FilterExecutions)
                {
                    var telemetrySnapshot = _telemetryStore.Record(execution.FilterName, execution.DurationMilliseconds);
                    if (telemetrySnapshot is not null)
                    {
                        _telemetryRecorder?.RecordFilterMetricSample(
                            telemetrySnapshot.FilterName,
                            telemetrySnapshot.AppliedCount,
                            telemetrySnapshot.LastDurationMilliseconds,
                            telemetrySnapshot.AverageDurationMilliseconds);
                    }
                }

                var encodeStopwatch = Stopwatch.StartNew();
                // Encode the updated bitmap into the processed frame payload
                var encodingSettings = configuration.ProcessedImageEncoding ?? new ImageEncodingSettings();
                var skiaFormat = ToSkiaFormat(encodingSettings.Format);
                var quality = Math.Clamp(encodingSettings.Quality, 1, 100);

                byte[] bytes;

                try
                {
                    using var data = compositionImage.Encode(skiaFormat, quality);
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

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    var filterBreakdown = composition.FilterExecutions.Count == 0
                        ? "none"
                        : string.Join(", ", composition.FilterExecutions.Select(t => $"{t.FilterName}:{t.DurationMilliseconds:F1}ms"));

                    _logger.LogDebug(
                        "Filter pipeline completed in {TotalMs}ms (surface {SurfaceMs}ms, encode {EncodeMs}ms). Filters: {Breakdown}.",
                        pipelineStopwatch.Elapsed.TotalMilliseconds,
                        composition.SurfaceMilliseconds,
                        encodeStopwatch.Elapsed.TotalMilliseconds,
                        filterBreakdown);
                }

                var processedFrame = new ProcessedFrame(
                    stackResult.FrameId,
                    stackResult.Timestamp,
                    stackResult.Exposure,
                    bytes,
                    ToContentType(encodingSettings.Format),
                    stackResult.FramesStacked,
                    stackResult.IntegrationMilliseconds,
                    composition.AppliedFilters,
                    ProcessingMilliseconds: 0,
                    ImmutableImage: compositionImage);

                processedFrame = processedFrame with
                {
                    FilterExecutions = composition.FilterExecutions,
                    SurfaceMilliseconds = composition.SurfaceMilliseconds
                };

                compositionImage = null;
                return processedFrame;
            }
            finally
            {
                compositionImage?.Dispose();
                disposableSourceImage?.Dispose();
                frameContext?.Dispose();
            }
        }

        public FilterMetricsSnapshot GetMetricsSnapshot() => _telemetryStore.Snapshot();

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
}