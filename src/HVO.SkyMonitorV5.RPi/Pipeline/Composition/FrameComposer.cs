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
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Pipeline.Composition;

/// <summary>
/// Centralizes creation of linear rendering surfaces and executes registered frame filters
/// to produce a composed <see cref="SKImage"/>.
/// </summary>
public sealed class FrameComposer
{
    private readonly SkiaSurfacePool _surfacePool;
    private readonly ILogger<FrameComposer> _logger;

    public FrameComposer(SkiaSurfacePool surfacePool, ILogger<FrameComposer> logger)
    {
        _surfacePool = surfacePool ?? throw new ArgumentNullException(nameof(surfacePool));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<FrameCompositionResult> ComposeAsync(
        SKImage sourceImage,
        FrameStackResult stackResult,
        CameraConfiguration configuration,
        FrameRenderContext? renderContext,
    IEnumerable<IFrameFilter> filters,
        CancellationToken cancellationToken)
    {
        if (sourceImage is null)
        {
            throw new ArgumentNullException(nameof(sourceImage));
        }

        if (stackResult is null)
        {
            throw new ArgumentNullException(nameof(stackResult));
        }

        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        if (filters is null)
        {
            throw new ArgumentNullException(nameof(filters));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var surfaceStopwatch = Stopwatch.StartNew();
        var surfaceLease = _surfacePool.RentLinearSurface(sourceImage.Width, sourceImage.Height);
        var surface = surfaceLease.Surface;
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawImage(sourceImage, 0, 0);
        surface.Canvas.Flush();
        surfaceStopwatch.Stop();

        var filterFrame = new FilterFrame(surfaceLease);
        SKBitmap? legacyBitmap = null;
        var legacyBitmapStale = true;
        var appliedFilters = new List<string>();
        var executions = new List<FilterExecution>();

        try
        {
            foreach (var filter in filters)
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
                appliedFilters.Add(filter.Name);

                try
                {
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
                    executions.Add(new FilterExecution(filter.Name, filterStopwatch.Elapsed.TotalMilliseconds));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    filterStopwatch.Stop();
                    _logger.LogError(ex, "Filter {Filter} ApplyAsync() failed; continuing.", filter.Name);
                }
            }

            var snapshot = filterFrame.SnapshotImage();
            return new FrameCompositionResult(
                snapshot,
                appliedFilters.ToArray(),
                executions.ToArray(),
                surfaceStopwatch.Elapsed.TotalMilliseconds);
        }
        finally
        {
            legacyBitmap?.Dispose();
            filterFrame.Dispose();
        }
    }

    private static bool IsFilterEnabled(CameraConfiguration configuration, string filterName)
    {
        var filters = configuration.FrameFilters;
        if (filters is null || filters.Count == 0)
        {
            return true;
        }

        return filters.Any(item => string.Equals(item, filterName, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record FrameCompositionResult(
    SKImage Image,
    IReadOnlyList<string> AppliedFilters,
    IReadOnlyList<FilterExecution> FilterExecutions,
    double SurfaceMilliseconds);

public readonly record struct FilterExecution(string FilterName, double DurationMilliseconds);
