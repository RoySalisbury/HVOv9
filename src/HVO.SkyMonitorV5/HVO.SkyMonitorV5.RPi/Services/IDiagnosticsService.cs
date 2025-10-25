using System.Threading;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.RPi.Models;

namespace HVO.SkyMonitorV5.RPi.Services;

public interface IDiagnosticsService
{
    Task<Result<BackgroundStackerMetricsResponse>> GetBackgroundStackerMetricsAsync(CancellationToken cancellationToken = default);

    Task<Result<FilterMetricsSnapshot>> GetFilterMetricsAsync(CancellationToken cancellationToken = default);

    Task<Result<BackgroundStackerHistoryResponse>> GetBackgroundStackerHistoryAsync(CancellationToken cancellationToken = default);

    Task<Result<SystemDiagnosticsSnapshot>> GetSystemDiagnosticsAsync(CancellationToken cancellationToken = default);

    Task<Result<RemoteDispatchMetricsSnapshot>> GetRemoteDispatchMetricsAsync(CancellationToken cancellationToken = default);

    Task<Result<RemoteDispatchHistoryResponse>> GetRemoteDispatchHistoryAsync(CancellationToken cancellationToken = default);

    Task<Result<ComposedFrameHistoryResponse>> GetComposedFrameHistoryAsync(CancellationToken cancellationToken = default);

    Task<Result<FrameExportMetricsSnapshot>> GetFrameExportMetricsAsync(CancellationToken cancellationToken = default);

    Task<Result<FrameExportHistoryResponse>> GetFrameExportHistoryAsync(CancellationToken cancellationToken = default);

    Task<Result<DataStoreMetricsSnapshot>> GetDataStoreMetricsAsync(CancellationToken cancellationToken = default);

    Task<Result<TelemetryEventPage>> GetTelemetryEventsAsync(
        long? afterId = null,
        long? beforeId = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default);
}
