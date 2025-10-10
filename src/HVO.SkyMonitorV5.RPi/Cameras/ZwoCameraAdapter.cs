#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HVO.SkyMonitorV5.RPi.Cameras;

/// <summary>
/// Placeholder adapter for physical ZWO cameras. Capture workflow will be implemented in a future iteration.
/// </summary>
public sealed class ZwoCameraAdapter : CameraAdapterBase
{
    public ZwoCameraAdapter(RigSpec rigSpec, IObservatoryClock observatoryClock, ILogger<ZwoCameraAdapter>? logger = null)
        : base(
            EnsureRigDescriptor(rigSpec),
            observatoryClock,
            logger ?? NullLogger<ZwoCameraAdapter>.Instance)
    {
    }

    protected override Task<Result<bool>> OnInitializeAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("ZWO adapter initializing for rig {Rig}.", Rig.Name);
        return Task.FromResult(Result<bool>.Success(true));
    }

    protected override Task<Result<bool>> OnShutdownAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("ZWO adapter shutdown requested for rig {Rig}.", Rig.Name);
        return Task.FromResult(Result<bool>.Success(true));
    }

    protected override Task<Result<AdapterFrame>> CaptureFrameAsync(ExposureSettings exposure, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<AdapterFrame>.Failure(new NotImplementedException("ZWO capture pipeline has not been implemented.")));
    }

    private static CameraDescriptor CreateDefaultDescriptor(RigSpec rig) => new(
        Manufacturer: "ZWO",
        Model: rig?.Name ?? "Unknown ZWO Camera",
        DriverVersion: "unversioned",
        AdapterName: nameof(ZwoCameraAdapter),
        Capabilities: new[] { "NativeHardware", "RequiresImplementation" });

    private static RigSpec EnsureRigDescriptor(RigSpec? rig)
    {
        if (rig is null)
        {
            throw new ArgumentNullException(nameof(rig));
        }

        return rig.Descriptor is not null
            ? rig
            : rig with { Descriptor = CreateDefaultDescriptor(rig) };
    }
}
