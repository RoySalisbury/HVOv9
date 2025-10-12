using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.Data.Telemetry.Entities;

namespace HVO.SkyMonitorV5.Data.Telemetry.Repositories;

public interface ISkyMonitorTelemetryRepository
{
    Task SaveRemoteDispatchAttemptAsync(RemoteDispatchAttemptEntity entity, CancellationToken cancellationToken = default);

    Task SaveBackgroundStackerSampleAsync(BackgroundStackerSampleEntity entity, CancellationToken cancellationToken = default);

    Task SaveCapturePacingSampleAsync(CapturePacingSampleEntity entity, CancellationToken cancellationToken = default);

    Task SaveProcessingQueueSampleAsync(ProcessingQueueSampleEntity entity, CancellationToken cancellationToken = default);

    Task SaveFilterMetricSampleAsync(FilterMetricSampleEntity entity, CancellationToken cancellationToken = default);

    Task SaveTelemetryEventAsync(TelemetryEventEntity entity, CancellationToken cancellationToken = default);

    Task<TelemetrySystemProfileEntity> UpsertSystemProfileAsync(TelemetrySystemProfileEntity entity, CancellationToken cancellationToken = default);
}
