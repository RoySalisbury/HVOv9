using System;
using System.Collections.Generic;
using System.Linq;
using HVO.SkyMonitorV5.RPi.Pipeline;
namespace HVO.SkyMonitorV5.RPi.Models;

/// <summary>
/// Represents the runtime configuration for the capture pipeline that can be updated through the API.
/// </summary>
public sealed record CameraConfiguration(
    bool EnableStacking,
    int StackingFrameCount,
    bool EnableImageOverlays,
    bool EnableMaskOverlay,
    int StackingBufferMinimumFrames,
    int StackingBufferIntegrationSeconds,
    IReadOnlyList<string> FrameFilters)
{
    public static CameraConfiguration FromOptions(Options.CameraPipelineOptions options) =>
        new(
            options.EnableStacking,
            options.StackingFrameCount,
            options.EnableImageOverlays,
            options.EnableMaskOverlay,
            options.StackingBufferMinimumFrames,
            options.StackingBufferIntegrationSeconds,
            BuildFilterList(options));

    public CameraConfiguration WithUpdates(UpdateCameraConfigurationRequest request)
    {
        var filtersProvided = request.FrameFilters is not null;

        IReadOnlyList<string> updatedFilters = FrameFilters ?? Array.Empty<string>();
        if (filtersProvided)
        {
            var requestFilters = request.FrameFilters!;
            updatedFilters = requestFilters.Count > 0
                ? requestFilters.Where(static filter => !string.IsNullOrWhiteSpace(filter))
                    .Select(static filter => filter.Trim())
                    .ToArray()
                : Array.Empty<string>();
        }
        else
        {
            var overlaysEnabled = request.EnableImageOverlays ?? EnableImageOverlays;
            var maskEnabled = request.EnableMaskOverlay ?? EnableMaskOverlay;

            var mutableFilters = updatedFilters.ToList();

            if (!overlaysEnabled)
            {
                mutableFilters.RemoveAll(static filter => IsOverlayFilterName(filter));
            }

            if (maskEnabled)
            {
                if (!mutableFilters.Any(filter => string.Equals(filter, FrameFilterNames.CircularMask, StringComparison.OrdinalIgnoreCase)))
                {
                    mutableFilters.Add(FrameFilterNames.CircularMask);
                }
            }
            else
            {
                mutableFilters.RemoveAll(filter => string.Equals(filter, FrameFilterNames.CircularMask, StringComparison.OrdinalIgnoreCase));
            }

            if (overlaysEnabled && mutableFilters.Count == 0)
            {
                mutableFilters.Add(FrameFilterNames.CardinalDirections);
                mutableFilters.Add(FrameFilterNames.CelestialAnnotations);
                mutableFilters.Add(FrameFilterNames.OverlayText);

                if (maskEnabled)
                {
                    mutableFilters.Add(FrameFilterNames.CircularMask);
                }
            }

            updatedFilters = mutableFilters.Count > 0 ? mutableFilters.ToArray() : Array.Empty<string>();
        }

        return this with
        {
            EnableStacking = request.EnableStacking ?? EnableStacking,
            StackingFrameCount = request.StackingFrameCount ?? StackingFrameCount,
            EnableImageOverlays = request.EnableImageOverlays ?? EnableImageOverlays,
            EnableMaskOverlay = request.EnableMaskOverlay ?? EnableMaskOverlay,
            StackingBufferMinimumFrames = request.StackingBufferMinimumFrames ?? StackingBufferMinimumFrames,
            StackingBufferIntegrationSeconds = request.StackingBufferIntegrationSeconds ?? StackingBufferIntegrationSeconds,
            FrameFilters = updatedFilters
        };
    }

    private static IReadOnlyList<string> BuildFilterList(Options.CameraPipelineOptions options)
    {
        if (options.FrameFilters is { Length: > 0 } configured)
        {
            return configured.ToArray();
        }

        var filters = new List<string>();

        if (options.EnableImageOverlays)
        {
            filters.Add(FrameFilterNames.CardinalDirections);
            filters.Add(FrameFilterNames.CelestialAnnotations);
            filters.Add(FrameFilterNames.OverlayText);
        }

        if (options.EnableMaskOverlay)
        {
            filters.Add(FrameFilterNames.CircularMask);
        }

        return filters.Count > 0 ? filters.ToArray() : Array.Empty<string>();
    }

    private static bool IsOverlayFilterName(string filterName) => string.Equals(filterName, FrameFilterNames.CardinalDirections, StringComparison.OrdinalIgnoreCase)
        || string.Equals(filterName, FrameFilterNames.CelestialAnnotations, StringComparison.OrdinalIgnoreCase)
        || string.Equals(filterName, FrameFilterNames.OverlayText, StringComparison.OrdinalIgnoreCase);
}
