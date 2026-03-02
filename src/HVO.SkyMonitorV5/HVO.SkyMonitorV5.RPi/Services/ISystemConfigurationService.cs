using System.Threading;
using System.Threading.Tasks;
using HVO.Core.Results;
using HVO.SkyMonitorV5.RPi.Models.System;

namespace HVO.SkyMonitorV5.RPi.Services;

public interface ISystemConfigurationService
{
    Task<Result<SystemObservatoryConfigurationResponse>> GetObservatoryAsync(CancellationToken cancellationToken);

    Task<Result<SystemObservatoryConfigurationResponse>> UpdateObservatoryAsync(UpdateSystemObservatoryRequest request, CancellationToken cancellationToken);

    Task<Result<SystemLocalApiConfigurationResponse>> GetLocalApiAsync(CancellationToken cancellationToken);

    Task<Result<SystemLocalApiConfigurationResponse>> UpdateLocalApiAsync(UpdateSystemLocalApiRequest request, CancellationToken cancellationToken);

    Task<Result<SystemTelemetryRetentionConfigurationResponse>> GetTelemetryRetentionAsync(CancellationToken cancellationToken);

    Task<Result<SystemTelemetryRetentionConfigurationResponse>> UpdateTelemetryRetentionAsync(UpdateSystemTelemetryRetentionRequest request, CancellationToken cancellationToken);

    Task<Result<RigRuntimeStatusResponse>> GetRigRuntimeStatusAsync(CancellationToken cancellationToken);

    Task<Result<RigRuntimeActionResponse>> ExecuteRigRuntimeActionAsync(RigRuntimeActionRequest request, CancellationToken cancellationToken);
}
