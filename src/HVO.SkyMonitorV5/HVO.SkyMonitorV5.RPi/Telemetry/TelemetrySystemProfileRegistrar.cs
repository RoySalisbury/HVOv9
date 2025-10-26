using System;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.Data.Telemetry.Entities;
using HVO.SkyMonitorV5.Data.Telemetry.Repositories;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Telemetry;

internal sealed class TelemetrySystemProfileRegistrar : ITelemetrySystemProfileRegistrar
{
    private readonly ITelemetrySystemProfileCollector _collector;
    private readonly ISkyMonitorTelemetryRepository _repository;
    private readonly ILogger<TelemetrySystemProfileRegistrar> _logger;

    public TelemetrySystemProfileRegistrar(
        ITelemetrySystemProfileCollector collector,
        ISkyMonitorTelemetryRepository repository,
        ILogger<TelemetrySystemProfileRegistrar> logger)
    {
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RegisterAsync(DateTimeOffset observedAtUtc, CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = _collector.Collect(observedAtUtc);
            var entity = Map(snapshot);

            var result = await _repository.UpsertSystemProfileAsync(entity, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Telemetry system profile registered for hash {SystemHash} (Machine:{Machine} OS:{OS}).", result.SystemHash, result.MachineName ?? result.HostName, result.OperatingSystem);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            _logger.TryLogOperationCanceled(ex, cancellationToken, "Telemetry system profile registration cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            if (_logger.TryLogOperationCanceled(ex, cancellationToken, "Telemetry system profile registration cancelled."))
            {
                throw;
            }

            _logger.LogError(ex, "Failed to register telemetry system profile metadata.");
            throw;
        }
    }

    private static TelemetrySystemProfileEntity Map(TelemetrySystemProfileSnapshot snapshot)
    {
        return new TelemetrySystemProfileEntity
        {
            SystemHash = snapshot.SystemHash,
            MachineName = snapshot.MachineName,
            HostName = snapshot.HostName,
            OperatingSystem = snapshot.OperatingSystem,
            OsArchitecture = snapshot.OsArchitecture,
            ProcessArchitecture = snapshot.ProcessArchitecture,
            FrameworkDescription = snapshot.FrameworkDescription,
            ProcessorCount = snapshot.ProcessorCount,
            TotalMemoryMegabytes = snapshot.TotalMemoryMegabytes,
            CpuModel = snapshot.CpuModel,
            HardwareModel = snapshot.HardwareModel,
            IsContainerized = snapshot.IsContainerized,
            AdditionalPropertiesJson = snapshot.AdditionalPropertiesJson,
            FirstSeenAtUtc = snapshot.FirstSeenAtUtc,
            LastSeenAtUtc = snapshot.LastSeenAtUtc
        };
    }
}
