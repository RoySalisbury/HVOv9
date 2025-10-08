#nullable enable
using HVO.SkyMonitorV5.RPi.Cameras.Optics;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HVO.SkyMonitorV5.RPi.Cameras.Projection;

/// <summary>
/// A complete camera+lens configuration the app can use at runtime.
/// </summary>
public sealed record RigSpec(
    string Name,
    SensorSpec Sensor,   // your existing SensorSpec type
    LensSpec Lens       // your existing LensSpec (ProjectionModel, FocalLengthMm, FovXDeg, ...)
);

/// <summary>
/// Handy ready-made rigs. Add more as you acquire lenses/cameras.
/// </summary>
public static class RigPresets
{
    /// <summary>
    /// ZWO ASI174MM + Fujinon FE185C086HA-1 (2.7mm, fisheye ~185°).
    ///  - ASI174MM native: 1936 x 1216 px, 5.86 µm pixel pitch.
    /// </summary>
    public static readonly RigSpec MockAsi174_Fujinon = new(
        Name: "MockASI174MM + Fujinon 2.7mm",
        Sensor: new SensorSpec(
            WidthPx: 1936,
            HeightPx: 1216,
            PixelSizeMicrons: 5.86
        ),
        Lens: new LensSpec(
            Model: ProjectionModel.Equidistant, // common for security fisheyes; adjust if calibration says otherwise
            FocalLengthMm: 2.7,
            FovXDeg: 185.0,
            FovYDeg: 185.0,
            RollDeg: 0.0,
            Name: "Fujinon FE185C086HA-1",
            Kind: LensKind.Fisheye
        )
    );
}

/// <summary>Read-only access to the active rig selected at startup.</summary>
public interface IRigProvider
{
    RigSpec Current { get; }
}

/// <summary>Internal implementation the hosted service populates at startup.</summary>
internal sealed class RigProvider : IRigProvider
{
    private RigSpec? _current;
    public RigSpec Current => _current ?? throw new InvalidOperationException("Rig not initialized.");

    internal void Set(RigSpec rig) => _current = rig ?? throw new ArgumentNullException(nameof(rig));
}

/// <summary>
/// Picks a RigSpec on startup and makes it available via IRigProvider.
/// </summary>
internal sealed class RigHostedService : IHostedService
{
    private readonly IRigProvider _provider;
    private readonly RigSpec _rig;
    private readonly ILogger<RigHostedService> _logger;

    public RigHostedService(IRigProvider provider, RigSpec rig, ILogger<RigHostedService> logger)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _rig = rig ?? throw new ArgumentNullException(nameof(rig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        (_provider as RigProvider)!.Set(_rig);

        _logger.LogInformation(
            "Rig initialized: {RigName} | Sensor {W}x{H} px @ {Pitch} µm | Lens {Lens} ({Model}, {FovX}×{FovY} deg, {Roll}° roll)",
            _rig.Name,
            _rig.Sensor.WidthPx, _rig.Sensor.HeightPx, _rig.Sensor.PixelSizeMicrons,
            _rig.Lens.Name, _rig.Lens.Model, _rig.Lens.FovXDeg, _rig.Lens.FovYDeg ?? _rig.Lens.FovXDeg, _rig.Lens.RollDeg);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public static class SkyRigServiceExtensions
{
    /// <summary>
    /// Registers the rig system and selects a preset rig (e.g., <see cref="RigPresets.MockAsi174_Fujinon"/>).
    /// </summary>
    public static IServiceCollection AddSkyRigPreset(this IServiceCollection services, RigSpec rig)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (rig is null) throw new ArgumentNullException(nameof(rig));

        // provider is a singleton container for the active rig
        services.TryAddSingleton<IRigProvider, RigProvider>();

        // store the chosen rig as a singleton value
        services.AddSingleton(rig);

        // hosted service applies the rig at startup and logs details
        services.AddHostedService<RigHostedService>();

        return services;
    }
}