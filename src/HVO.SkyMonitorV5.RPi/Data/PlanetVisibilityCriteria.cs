#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using HVO.SkyMonitorV5.RPi.Cameras.MockCamera;
using HVO.SkyMonitorV5.RPi.Options;

namespace HVO.SkyMonitorV5.RPi.Data;

/// <summary>
/// Represents the selection criteria used when retrieving visible planet data.
/// </summary>
public sealed class PlanetVisibilityCriteria
{
    private static readonly PlanetBody[] CorePlanets =
    {
        PlanetBody.Mercury,
        PlanetBody.Venus,
        PlanetBody.Mars,
        PlanetBody.Jupiter,
        PlanetBody.Saturn
    };

    private readonly IReadOnlyCollection<PlanetBody>? _allowedBodies;
    private readonly HashSet<PlanetBody>? _allowedLookup;

    public bool IncludePlanets { get; }
    public bool IncludeMoon { get; }
    public bool IncludeOuterPlanets { get; }
    public bool IncludeSun { get; }

    public IReadOnlyCollection<PlanetBody>? AllowedBodies => _allowedBodies;

    public PlanetVisibilityCriteria(
        bool includePlanets,
        bool includeMoon,
        bool includeOuterPlanets,
        bool includeSun,
        IReadOnlyCollection<PlanetBody>? allowedBodies = null)
    {
        IncludePlanets = includePlanets;
        IncludeMoon = includeMoon;
        IncludeOuterPlanets = includeOuterPlanets;
        IncludeSun = includeSun;

        if (allowedBodies is { Count: > 0 })
        {
            var distinct = allowedBodies.Distinct().ToArray();
            _allowedBodies = Array.AsReadOnly(distinct);
            _allowedLookup = distinct.ToHashSet();
        }
    }

    public bool ShouldCompute => IncludePlanets || IncludeMoon || IncludeOuterPlanets || IncludeSun;

    public bool IsBodyEnabled(PlanetBody body)
    {
        if (_allowedLookup is not null && !_allowedLookup.Contains(body))
        {
            return false;
        }

        return body switch
        {
            PlanetBody.Moon => IncludeMoon,
            PlanetBody.Sun => IncludeSun,
            PlanetBody.Uranus or PlanetBody.Neptune => IncludeOuterPlanets,
            _ => IncludePlanets
        };
    }

    public IReadOnlyList<PlanetBody> ResolveBodies()
    {
        if (!ShouldCompute)
        {
            return Array.Empty<PlanetBody>();
        }

        var bodies = new List<PlanetBody>();

        if (IncludePlanets)
        {
            foreach (var body in CorePlanets)
            {
                TryAddBody(body, bodies);
            }
        }

        if (IncludeOuterPlanets)
        {
            TryAddBody(PlanetBody.Uranus, bodies);
            TryAddBody(PlanetBody.Neptune, bodies);
        }

        if (IncludeMoon)
        {
            TryAddBody(PlanetBody.Moon, bodies);
        }

        if (IncludeSun)
        {
            TryAddBody(PlanetBody.Sun, bodies);
        }

        if (_allowedLookup is not null)
        {
            foreach (var body in _allowedLookup)
            {
                TryAddBody(body, bodies);
            }
        }

        return bodies.Count == 0 ? Array.Empty<PlanetBody>() : bodies;
    }

    public static PlanetVisibilityCriteria FromOptions(
        StarCatalogOptions options,
        IReadOnlyCollection<PlanetBody>? allowedBodies = null)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        return new PlanetVisibilityCriteria(
            includePlanets: options.IncludePlanets,
            includeMoon: options.IncludeMoon,
            includeOuterPlanets: options.IncludeOuterPlanets,
            includeSun: options.IncludeSun,
            allowedBodies: allowedBodies);
    }

    private void TryAddBody(PlanetBody candidate, ICollection<PlanetBody> destination)
    {
        if (!IsBodyEnabled(candidate))
        {
            return;
        }

        if (!destination.Contains(candidate))
        {
            destination.Add(candidate);
        }
    }
}
