using System;

namespace HVO.SkyMonitorV5.RPi.Infrastructure;

public interface IDataStoreBootstrapStatus
{
    void ReportConfigurationSuccess(string databasePath, DateTimeOffset startedAtUtc, DateTimeOffset completedAtUtc);

    void ReportConfigurationFailure(string databasePath, DateTimeOffset startedAtUtc, Exception exception);

    void ReportTelemetrySuccess(string databasePath, DateTimeOffset startedAtUtc, DateTimeOffset completedAtUtc);

    void ReportTelemetryFailure(string databasePath, DateTimeOffset startedAtUtc, Exception exception);

    DataStoreBootstrapSnapshot GetSnapshot();
}

public sealed class DataStoreBootstrapStatus : IDataStoreBootstrapStatus
{
    private readonly object _sync = new();
    private DataStoreBootstrapState _configuration;
    private DataStoreBootstrapState _telemetry;

    public DataStoreBootstrapStatus()
    {
        _configuration = DataStoreBootstrapState.NotRun("configuration/sm-config.db");
        _telemetry = DataStoreBootstrapState.NotRun("telemetry/sm-telemetry.db");
    }

    public void ReportConfigurationSuccess(string databasePath, DateTimeOffset startedAtUtc, DateTimeOffset completedAtUtc)
    {
        ArgumentException.ThrowIfNullOrEmpty(databasePath);
        lock (_sync)
        {
            _configuration = DataStoreBootstrapState.Success(databasePath, startedAtUtc, completedAtUtc);
        }
    }

    public void ReportConfigurationFailure(string databasePath, DateTimeOffset startedAtUtc, Exception exception)
    {
        ArgumentException.ThrowIfNullOrEmpty(databasePath);
        ArgumentNullException.ThrowIfNull(exception);
        lock (_sync)
        {
            _configuration = DataStoreBootstrapState.Failure(databasePath, startedAtUtc, exception.Message);
        }
    }

    public void ReportTelemetrySuccess(string databasePath, DateTimeOffset startedAtUtc, DateTimeOffset completedAtUtc)
    {
        ArgumentException.ThrowIfNullOrEmpty(databasePath);
        lock (_sync)
        {
            _telemetry = DataStoreBootstrapState.Success(databasePath, startedAtUtc, completedAtUtc);
        }
    }

    public void ReportTelemetryFailure(string databasePath, DateTimeOffset startedAtUtc, Exception exception)
    {
        ArgumentException.ThrowIfNullOrEmpty(databasePath);
        ArgumentNullException.ThrowIfNull(exception);
        lock (_sync)
        {
            _telemetry = DataStoreBootstrapState.Failure(databasePath, startedAtUtc, exception.Message);
        }
    }

    public DataStoreBootstrapSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new DataStoreBootstrapSnapshot(_configuration, _telemetry);
        }
    }
}

public readonly record struct DataStoreBootstrapSnapshot(DataStoreBootstrapState Configuration, DataStoreBootstrapState Telemetry);

public sealed record DataStoreBootstrapState(
    string DatabasePath,
    bool Ran,
    bool Succeeded,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? ErrorMessage)
{
    public static DataStoreBootstrapState NotRun(string databasePath)
        => new(databasePath, Ran: false, Succeeded: false, StartedAtUtc: null, CompletedAtUtc: null, ErrorMessage: null);

    public static DataStoreBootstrapState Success(string databasePath, DateTimeOffset startedAtUtc, DateTimeOffset completedAtUtc)
        => new(databasePath, Ran: true, Succeeded: true, StartedAtUtc: startedAtUtc, CompletedAtUtc: completedAtUtc, ErrorMessage: null);

    public static DataStoreBootstrapState Failure(string databasePath, DateTimeOffset startedAtUtc, string errorMessage)
        => new(databasePath, Ran: true, Succeeded: false, StartedAtUtc: startedAtUtc, CompletedAtUtc: null, ErrorMessage: errorMessage);
}
