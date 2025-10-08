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

            var frame = stackResult.Frame;
            using var bitmap = DecodeBitmap(frame);

            foreach (var filter in _filters)
            {
                cancellationToken.ThrowIfCancellationRequested();

                bool apply = false;
                try
                {
                    apply = filter.ShouldApply(configuration);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Filter {Filter} ShouldApply() threw.", filter.Name);
                }

                if (!apply) continue;

                try
                {
                    await filter.ApplyAsync(bitmap, stackResult, configuration, cancellationToken).ConfigureAwait(false);
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

            // Encode the updated bitmap into the processed frame payload
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);
            var bytes = data.ToArray();

            return new ProcessedFrame(
                frame.Timestamp,
                frame.Exposure,
                bytes,
                "image/png",
                stackResult.FramesCombined,
                stackResult.IntegrationMilliseconds);
        }

        private static SKBitmap DecodeBitmap(CameraFrame frame)
        {
            // Prefer the raw pixel buffer if present for lossless overlays
            if (frame.RawPixelBuffer is { } buf)
            {
                var bmp = new SKBitmap(new SKImageInfo(buf.Width, buf.Height, SKColorType.Bgra8888));
                var span = bmp.GetPixelSpan();
                var src = buf.PixelBytes;
                if (src.Length == span.Length)
                {
                    src.CopyTo(span);
                }
                else
                {
                    // Fallback to decode if the buffer metadata mismatched (shouldn't happen)
                    return SKBitmap.Decode(frame.ImageBytes) ?? new SKBitmap(1280, 960);
                }
                return bmp;
            }

            return SKBitmap.Decode(frame.ImageBytes) ?? new SKBitmap(1280, 960);
        }
    }
}