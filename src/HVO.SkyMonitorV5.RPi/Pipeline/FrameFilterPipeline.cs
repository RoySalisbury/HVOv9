#nullable enable
using System;
using System.Collections.Generic;
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

            var copy = stackResult.StackedImage.Copy();
            if (copy is null)
            {
                throw new InvalidOperationException("Unable to copy raw frame image for filter processing.");
            }

            using var bitmap = copy;
            var renderContext = stackResult.Context is { } context ? new FrameRenderContext(context) : null;

            var appliedFilters = new List<string>();

            try
            {
                foreach (var filter in _filters)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!IsFilterEnabled(configuration, filter.Name))
                    {
                        continue;
                    }

                    bool apply = false;
                    try
                    {
                        apply = filter.ShouldApply(configuration);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Filter {Filter} ShouldApply() threw.", filter.Name);
                    }

                    if (!apply)
                    {
                        continue;
                    }

                    try
                    {
                        appliedFilters.Add(filter.Name);
                        await filter.ApplyAsync(bitmap, stackResult, configuration, renderContext, cancellationToken).ConfigureAwait(false);
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
            }
            finally
            {
                renderContext?.FrameContext.Dispose();
            }

            // Encode the updated bitmap into the processed frame payload
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);
            var bytes = data.ToArray();

            return new ProcessedFrame(
                stackResult.Timestamp,
                stackResult.Exposure,
                bytes,
                "image/png",
                stackResult.FramesStacked,
                stackResult.IntegrationMilliseconds,
                appliedFilters,
                ProcessingMilliseconds: 0);
        }

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
    }
}