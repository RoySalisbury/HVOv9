#nullable enable

using HVO;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Models;

namespace HVO.SkyMonitorV5.RPi.Cameras.Zwo;

/// <summary>
/// Placeholder implementation for a ZWO ASI camera adapter. The concrete integration should wrap the
/// HVO.ZWOOptical.ASISDK library and translate frames into the shared <see cref="CapturedImage"/> model.
/// </summary>
public sealed class ZwoCameraAdapter : ICameraAdapter
{
    public ZwoCameraAdapter(RigSpec rig)
    {
        Rig = rig switch
        {
            null => throw new ArgumentNullException(nameof(rig)),
            { Descriptor: null } => rig with
            {
                Descriptor = new CameraDescriptor(
                    Manufacturer: "ZWO",
                    Model: rig.Name,
                    DriverVersion: "unversioned",
                    AdapterName: nameof(ZwoCameraAdapter),
                    Capabilities: new[] { "Native", "StackingCompatible", "HighSpeed", "Cooled" })
            },
            _ => rig
        };
    }

    public RigSpec Rig { get; }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public Task<Result<bool>> InitializeAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<bool>.Failure(new NotImplementedException("ZWO camera integration is not yet implemented.")));
    }

    public Task<Result<bool>> ShutdownAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<bool>.Success(true));
    }

    public Task<Result<CapturedImage>> CaptureAsync(ExposureSettings exposure, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<CapturedImage>.Failure(new NotImplementedException("ZWO camera integration is not yet implemented.")));
    }
}
