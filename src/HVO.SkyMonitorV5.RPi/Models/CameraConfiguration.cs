using System;
using System.Collections.Generic;
using System.Linq;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Pipeline;

namespace HVO.SkyMonitorV5.RPi.Models;

/// <summary>
/// Represents the runtime configuration for the capture pipeline that can be updated through the API.
/// </summary>
public sealed record CameraConfiguration(
    bool EnableStacking,
    int StackingFrameCount,
    bool EnableImageOverlays,
    bool EnableCircularApertureMask,
    int StackingBufferMinimumFrames,
    int StackingBufferIntegrationSeconds,
    IReadOnlyList<string> FrameFilters)
{
    public static CameraConfiguration FromOptions(CameraPipelineOptions options)
    {
        var filters = BuildFilterList(options);
        var maskEnabled = filters.Any(static filter => string.Equals(filter, FrameFilterNames.CircularApertureMask, StringComparison.OrdinalIgnoreCase));
        var constrainedFilters = ApplyOverlayConstraints(filters, options.EnableImageOverlays, maskEnabled);
        var finalMaskEnabled = constrainedFilters.Any(static filter => string.Equals(filter, FrameFilterNames.CircularApertureMask, StringComparison.OrdinalIgnoreCase));

        return new CameraConfiguration(
            options.EnableStacking,
            options.StackingFrameCount,
            options.EnableImageOverlays,
            finalMaskEnabled,
            options.StackingBufferMinimumFrames,
            options.StackingBufferIntegrationSeconds,
            constrainedFilters);
    }

    public CameraConfiguration WithUpdates(UpdateCameraConfigurationRequest request)
    {
        var filtersProvided = request.FrameFilters is not null;

        IReadOnlyList<string> updatedFilters = FrameFilters ?? Array.Empty<string>();
        if (filtersProvided)
        {
            var requestFilters = request.FrameFilters!;
            updatedFilters = requestFilters.Count > 0
                ? NormalizeFilterList(requestFilters)
                : Array.Empty<string>();
        }
        else
        {
            var overlaysEnabled = request.EnableImageOverlays ?? EnableImageOverlays;
            var maskEnabled = request.EnableCircularApertureMask ?? EnableCircularApertureMask;

            var mutableFilters = updatedFilters.ToList();

            if (!overlaysEnabled)
            {
                mutableFilters.RemoveAll(static filter => IsOverlayFilterName(filter));
            }

            if (maskEnabled)
            {
                if (!mutableFilters.Any(filter => string.Equals(filter, FrameFilterNames.CircularApertureMask, StringComparison.OrdinalIgnoreCase)))
                {
                    mutableFilters.Add(FrameFilterNames.CircularApertureMask);
                }
            }
            else
            {
                mutableFilters.RemoveAll(filter => string.Equals(filter, FrameFilterNames.CircularApertureMask, StringComparison.OrdinalIgnoreCase));
            }

            if (overlaysEnabled && mutableFilters.Count == 0)
            {
                mutableFilters.Add(FrameFilterNames.CardinalDirections);
                mutableFilters.Add(FrameFilterNames.CelestialAnnotations);
                mutableFilters.Add(FrameFilterNames.OverlayText);

                if (maskEnabled)
                {
                    mutableFilters.Add(FrameFilterNames.CircularApertureMask);
                }
            }

            updatedFilters = mutableFilters.Count > 0 ? NormalizeFilterList(mutableFilters) : Array.Empty<string>();
        }

    var overlaysEnabledResult = request.EnableImageOverlays ?? EnableImageOverlays;
    var maskEnabledResult = request.EnableCircularApertureMask ?? EnableCircularApertureMask;

        updatedFilters = ApplyOverlayConstraints(updatedFilters, overlaysEnabledResult, maskEnabledResult);

        return this with
        {
            EnableStacking = request.EnableStacking ?? EnableStacking,
            StackingFrameCount = request.StackingFrameCount ?? StackingFrameCount,
            EnableImageOverlays = overlaysEnabledResult,
            EnableCircularApertureMask = maskEnabledResult,
            StackingBufferMinimumFrames = request.StackingBufferMinimumFrames ?? StackingBufferMinimumFrames,
            StackingBufferIntegrationSeconds = request.StackingBufferIntegrationSeconds ?? StackingBufferIntegrationSeconds,
            FrameFilters = updatedFilters
        };
    }

    private static IReadOnlyList<string> BuildFilterList(Options.CameraPipelineOptions options)
    {
        var configuredFilters = BuildFromFilterOptions(options.Filters);
        if (configuredFilters.Count > 0)
        {
            return configuredFilters;
        }

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

        return filters.Count > 0 ? NormalizeFilterList(filters) : Array.Empty<string>();
    }

    private static bool IsOverlayFilterName(string filterName) => string.Equals(filterName, FrameFilterNames.CardinalDirections, StringComparison.OrdinalIgnoreCase)
        || string.Equals(filterName, FrameFilterNames.CelestialAnnotations, StringComparison.OrdinalIgnoreCase)
        || string.Equals(filterName, FrameFilterNames.OverlayText, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> ApplyOverlayConstraints(IReadOnlyList<string>? filters, bool enableOverlays, bool enableMask)
    {
        if (filters is null || filters.Count == 0)
        {
            return Array.Empty<string>();
        }

        var mutable = filters.ToList();

        if (!enableOverlays)
        {
            mutable.RemoveAll(IsOverlayFilterName);
        }

        if (!enableMask)
        {
            mutable.RemoveAll(static filter => string.Equals(filter, FrameFilterNames.CircularApertureMask, StringComparison.OrdinalIgnoreCase));
        }

        return mutable.Count > 0 ? NormalizeFilterList(mutable) : Array.Empty<string>();
    }

    private static IReadOnlyList<string> BuildFromFilterOptions(FrameFilterOption[]? filterOptions)
    {
        if (filterOptions is not { Length: > 0 })
        {
            return Array.Empty<string>();
        }

        var enabledFilters = filterOptions
            .Where(static option => option.Enabled && !string.IsNullOrWhiteSpace(option.Name))
            .OrderBy(static option => option.Order)
            .ThenBy(static option => option.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static option => option.Name.Trim());

        return NormalizeFilterList(enabledFilters);
    }

    private static IReadOnlyList<string> NormalizeFilterList(IEnumerable<string> filters)
        => filters
            .Where(static filter => !string.IsNullOrWhiteSpace(filter))
            .Select(static filter => filter.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
