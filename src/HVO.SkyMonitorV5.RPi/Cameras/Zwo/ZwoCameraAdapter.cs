#nullable enable

using HVO;
using HVO.SkyMonitorV5.RPi.Models;

namespace HVO.SkyMonitorV5.RPi.Cameras.Zwo;

/// <summary>
/// Placeholder implementation for a ZWO ASI camera adapter. The concrete integration should wrap the
/// HVO.ZWOOptical.ASISDK library and translate frames into the shared <see cref="CameraFrame"/> model.
/// </summary>
public sealed class ZwoCameraAdapter : ICameraAdapter
{
    public CameraDescriptor Descriptor { get; } = new(
        Manufacturer: "ZWO",
        Model: "ASI-Series",
        DriverVersion: "TBD",
        AdapterName: nameof(ZwoCameraAdapter),
        Capabilities: new[] { "Native", "StackingCompatible", "HighSpeed" });

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public Task<Result<bool>> InitializeAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<bool>.Failure(new NotImplementedException("ZWO camera integration is not yet implemented.")));
    }

    public Task<Result<bool>> ShutdownAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<bool>.Success(true));
    }

    public Task<Result<CameraFrame>> CaptureAsync(ExposureSettings exposure, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<CameraFrame>.Failure(new NotImplementedException("ZWO camera integration is not yet implemented.")));
    }
}
