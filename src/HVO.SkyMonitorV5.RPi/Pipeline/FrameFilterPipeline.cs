#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Pipeline.Filters;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Pipeline;

public sealed class FrameFilterPipeline : IFrameFilterPipeline
{
    private readonly IReadOnlyDictionary<string, IFrameFilter> _filters;
    private readonly ILogger<FrameFilterPipeline> _logger;

    public FrameFilterPipeline(IEnumerable<IFrameFilter> filters, ILogger<FrameFilterPipeline> logger)
    {
        _filters = filters.ToDictionary(filter => filter.Name, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    public async Task<ProcessedFrame> ProcessAsync(FrameStackResult stackResult, CameraConfiguration configuration, CancellationToken cancellationToken)
    {
        var frame = stackResult.Frame;
        using var bitmap = SKBitmap.Decode(frame.ImageBytes) ?? throw new InvalidOperationException("Unable to decode camera frame.");

        var filterSequence = configuration.FrameFilters ?? Array.Empty<string>();
        if (filterSequence.Count == 0)
        {
            _logger.LogTrace("No frame filters configured for the current camera. Returning original frame without additional processing.");
        }

        foreach (var filterName in filterSequence)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_filters.TryGetValue(filterName, out var filter))
            {
                _logger.LogWarning("Frame filter {FilterName} is configured but not registered.", filterName);
                continue;
            }

            if (!filter.ShouldApply(configuration))
            {
                _logger.LogTrace("Skipping frame filter {FilterName} due to configuration state.", filterName);
                continue;
            }

            _logger.LogDebug("Applying frame filter {FilterName}.", filterName);
            await filter.ApplyAsync(bitmap, stackResult, configuration, cancellationToken);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        var processedExposure = stackResult.FramesCombined > 1
            ? frame.Exposure with { ExposureMilliseconds = stackResult.IntegrationMilliseconds }
            : frame.Exposure;

        return new ProcessedFrame(
            frame.Timestamp,
            processedExposure,
            data.ToArray(),
            "image/jpeg",
            stackResult.FramesCombined,
            stackResult.IntegrationMilliseconds);
    }
}
