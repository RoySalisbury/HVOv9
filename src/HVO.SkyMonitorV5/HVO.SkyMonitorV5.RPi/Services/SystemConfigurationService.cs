using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HVO.Core.Results;
using HVO.SkyMonitorV5.Data.Configurations;
using HVO.SkyMonitorV5.Data.Configurations.Entities;
using HVO.SkyMonitorV5.RPi.Cameras.Acquisition;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Models.System;
using HVO.SkyMonitorV5.RPi.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OptionsDefaults = Microsoft.Extensions.Options.Options;

namespace HVO.SkyMonitorV5.RPi.Services;

public sealed class SystemConfigurationService : ISystemConfigurationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);

    private readonly IDbContextFactory<SkyMonitorConfigurationContext> _contextFactory;
    private readonly IConfigurationSnapshotInvalidator _snapshotInvalidator;
    private readonly IOptionsMonitor<ObservatoryLocationOptions> _observatoryMonitor;
    private readonly IOptionsMonitor<LocalApiClientOptions> _localApiMonitor;
    private readonly IOptionsMonitor<SkyMonitorTelemetryRetentionOptions> _telemetryMonitor;
    private readonly IOptionsMonitorCache<ObservatoryLocationOptions> _observatoryCache;
    private readonly IOptionsMonitorCache<LocalApiClientOptions> _localApiCache;
    private readonly IOptionsMonitorCache<SkyMonitorTelemetryRetentionOptions> _telemetryCache;
    private readonly TimeProvider _timeProvider;
    private readonly IRigRuntimeUpdater _runtimeUpdater;
    private readonly IRigAcquisitionAdapter _rigAdapter;
    private readonly ILogger<SystemConfigurationService>? _logger;

    public SystemConfigurationService(
        IDbContextFactory<SkyMonitorConfigurationContext> contextFactory,
        IConfigurationSnapshotInvalidator snapshotInvalidator,
        IOptionsMonitor<ObservatoryLocationOptions> observatoryMonitor,
        IOptionsMonitor<LocalApiClientOptions> localApiMonitor,
        IOptionsMonitor<SkyMonitorTelemetryRetentionOptions> telemetryMonitor,
        IOptionsMonitorCache<ObservatoryLocationOptions> observatoryCache,
        IOptionsMonitorCache<LocalApiClientOptions> localApiCache,
        IOptionsMonitorCache<SkyMonitorTelemetryRetentionOptions> telemetryCache,
        TimeProvider timeProvider,
        IRigRuntimeUpdater runtimeUpdater,
        IRigAcquisitionAdapter rigAdapter,
        ILogger<SystemConfigurationService>? logger = null)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _snapshotInvalidator = snapshotInvalidator ?? throw new ArgumentNullException(nameof(snapshotInvalidator));
        _observatoryMonitor = observatoryMonitor ?? throw new ArgumentNullException(nameof(observatoryMonitor));
        _localApiMonitor = localApiMonitor ?? throw new ArgumentNullException(nameof(localApiMonitor));
        _telemetryMonitor = telemetryMonitor ?? throw new ArgumentNullException(nameof(telemetryMonitor));
        _observatoryCache = observatoryCache ?? throw new ArgumentNullException(nameof(observatoryCache));
        _localApiCache = localApiCache ?? throw new ArgumentNullException(nameof(localApiCache));
        _telemetryCache = telemetryCache ?? throw new ArgumentNullException(nameof(telemetryCache));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _runtimeUpdater = runtimeUpdater ?? throw new ArgumentNullException(nameof(runtimeUpdater));
        _rigAdapter = rigAdapter ?? throw new ArgumentNullException(nameof(rigAdapter));
        _logger = logger;
    }

    public async Task<Result<SystemObservatoryConfigurationResponse>> GetObservatoryAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var entity = await context.ObservatorySites
                .AsNoTracking()
                .OrderBy(site => site.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return Result<SystemObservatoryConfigurationResponse>.Failure(new InvalidOperationException("Observatory configuration is not initialized."));
            }

            return Result<SystemObservatoryConfigurationResponse>.Success(MapObservatory(entity));
        }
        catch (Exception ex)
        {
            return Result<SystemObservatoryConfigurationResponse>.Failure(ex);
        }
    }

    public async Task<Result<SystemObservatoryConfigurationResponse>> UpdateObservatoryAsync(UpdateSystemObservatoryRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var entity = await ResolveObservatoryEntityAsync(context, request.Id, cancellationToken).ConfigureAwait(false);

            if (entity is null)
            {
                var message = request.Id > 0
                    ? $"Observatory configuration with ID {request.Id} was not found."
                    : "Observatory configuration is not initialized.";

                return Result<SystemObservatoryConfigurationResponse>.Failure(new InvalidOperationException(message));
            }

            var normalizedSlug = request.Slug.Trim();
            var normalizedName = request.Name.Trim();

            if (request.Revision > 0 && request.Revision != entity.Revision)
            {
                return Result<SystemObservatoryConfigurationResponse>.Failure(new InvalidOperationException(
                    $"Observatory configuration revision mismatch. Expected {entity.Revision}, received {request.Revision}."));
            }

            if (await context.ObservatorySites
                .AnyAsync(site => site.Id != entity.Id && site.Slug == normalizedSlug, cancellationToken)
                        .ConfigureAwait(false))
            {
                return Result<SystemObservatoryConfigurationResponse>.Failure(new InvalidOperationException($"An observatory with slug '{normalizedSlug}' already exists."));
            }

            if (!TryValidateTimeZone(request.TimeZoneId, out var timeZoneError))
            {
                return Result<SystemObservatoryConfigurationResponse>.Failure(timeZoneError!);
            }

            entity.Slug = normalizedSlug;
            entity.Name = normalizedName;
            entity.LatitudeDegrees = request.LatitudeDegrees;
            entity.LongitudeDegrees = request.LongitudeDegrees;
            entity.TimeZoneId = request.TimeZoneId.Trim();
            entity.Revision = entity.Revision <= 0 ? 1 : entity.Revision + 1;

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            InvalidateCaches();

            return Result<SystemObservatoryConfigurationResponse>.Success(MapObservatory(entity));
        }
        catch (Exception ex)
        {
            return Result<SystemObservatoryConfigurationResponse>.Failure(ex);
        }
    }

    private static async Task<ObservatorySiteEntity?> ResolveObservatoryEntityAsync(
        SkyMonitorConfigurationContext context,
        int requestedId,
        CancellationToken cancellationToken)
    {
        if (requestedId > 0)
        {
            return await context.ObservatorySites
                .AsTracking()
                .FirstOrDefaultAsync(site => site.Id == requestedId, cancellationToken)
                .ConfigureAwait(false);
        }

        return await context.ObservatorySites
            .AsTracking()
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<SystemLocalApiConfigurationResponse>> GetLocalApiAsync(CancellationToken cancellationToken)
    {
        try
        {
            var fallback = _localApiMonitor.CurrentValue ?? new LocalApiClientOptions();

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var entity = await context.SystemSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == SystemSettingKeys.LocalApi, cancellationToken)
                .ConfigureAwait(false);

            var options = TryDeserialize(entity?.PayloadJson, fallback);
            var revision = entity?.Revision ?? 0;
            return Result<SystemLocalApiConfigurationResponse>.Success(MapLocalApi(options, revision));
        }
        catch (Exception ex)
        {
            return Result<SystemLocalApiConfigurationResponse>.Failure(ex);
        }
    }

    public async Task<Result<SystemLocalApiConfigurationResponse>> UpdateLocalApiAsync(UpdateSystemLocalApiRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var normalizedHeader = string.IsNullOrWhiteSpace(request.ApiKeyHeaderName)
                ? "X-Api-Key"
                : request.ApiKeyHeaderName.Trim();

            var sanitizedBaseAddress = string.IsNullOrWhiteSpace(request.BaseAddress)
                ? null
                : request.BaseAddress.Trim();

            var sanitizedApiKey = string.IsNullOrWhiteSpace(request.ApiKey)
                ? null
                : request.ApiKey.Trim();

            if (sanitizedBaseAddress is not null && !Uri.TryCreate(sanitizedBaseAddress, UriKind.Absolute, out _))
            {
                return Result<SystemLocalApiConfigurationResponse>.Failure(new InvalidOperationException("Base address must be an absolute URI when provided."));
            }

            var timeoutSeconds = Math.Clamp(request.TimeoutSeconds, 0.1d, 600d);
            var configured = new LocalApiClientOptions
            {
                BaseAddress = sanitizedBaseAddress,
                ApiKey = sanitizedApiKey,
                ApiKeyHeaderName = normalizedHeader,
                Timeout = TimeSpan.FromSeconds(timeoutSeconds)
            };

            var revision = await UpsertSystemSettingAsync(SystemSettingKeys.LocalApi, configured, request.Revision, cancellationToken).ConfigureAwait(false);

            InvalidateCaches(localApi: true);

            return Result<SystemLocalApiConfigurationResponse>.Success(MapLocalApi(configured, revision));
        }
        catch (Exception ex)
        {
            return Result<SystemLocalApiConfigurationResponse>.Failure(ex);
        }
    }

    public async Task<Result<SystemTelemetryRetentionConfigurationResponse>> GetTelemetryRetentionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var fallback = _telemetryMonitor.CurrentValue ?? new SkyMonitorTelemetryRetentionOptions();

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var entity = await context.SystemSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == SystemSettingKeys.TelemetryRetention, cancellationToken)
                .ConfigureAwait(false);

            var options = TryDeserialize(entity?.PayloadJson, fallback);
            var revision = entity?.Revision ?? 0;
            return Result<SystemTelemetryRetentionConfigurationResponse>.Success(MapTelemetry(options, revision));
        }
        catch (Exception ex)
        {
            return Result<SystemTelemetryRetentionConfigurationResponse>.Failure(ex);
        }
    }

    public async Task<Result<SystemTelemetryRetentionConfigurationResponse>> UpdateTelemetryRetentionAsync(UpdateSystemTelemetryRetentionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var configured = new SkyMonitorTelemetryRetentionOptions
            {
                SweepInterval = TimeSpan.FromSeconds(Math.Clamp(request.SweepIntervalSeconds, 0.1d, 86400d)),
                VacuumAfterPurge = request.VacuumAfterPurge,
                RemoteDispatch = ConvertPolicy(request.RemoteDispatch, TelemetryRetentionPolicy.Create(TimeSpan.FromDays(30), 5_000)),
                FrameExports = ConvertPolicy(request.FrameExports, TelemetryRetentionPolicy.Create(TimeSpan.FromDays(30), 20_000)),
                BackgroundStacker = ConvertPolicy(request.BackgroundStacker, TelemetryRetentionPolicy.Create(TimeSpan.FromDays(14), 15_000)),
                CapturePacing = ConvertPolicy(request.CapturePacing, TelemetryRetentionPolicy.Create(TimeSpan.FromDays(14), 15_000)),
                ProcessingQueue = ConvertPolicy(request.ProcessingQueue, TelemetryRetentionPolicy.Create(TimeSpan.FromDays(14), 15_000)),
                FilterMetrics = ConvertPolicy(request.FilterMetrics, TelemetryRetentionPolicy.Create(TimeSpan.FromDays(30), 5_000)),
                TelemetryEvents = ConvertPolicy(request.TelemetryEvents, TelemetryRetentionPolicy.Create(TimeSpan.FromDays(30), 20_000))
            };

            var revision = await UpsertSystemSettingAsync(SystemSettingKeys.TelemetryRetention, configured, request.Revision, cancellationToken).ConfigureAwait(false);

            InvalidateCaches(telemetry: true);

            return Result<SystemTelemetryRetentionConfigurationResponse>.Success(MapTelemetry(configured, revision));
        }
        catch (Exception ex)
        {
            return Result<SystemTelemetryRetentionConfigurationResponse>.Failure(ex);
        }
    }

    public Task<Result<RigRuntimeStatusResponse>> GetRigRuntimeStatusAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<Result<RigRuntimeStatusResponse>>(cancellationToken);
        }

        try
        {
            var status = BuildRuntimeStatus("Runtime status retrieved.");
            return Task.FromResult(Result<RigRuntimeStatusResponse>.Success(status));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<RigRuntimeStatusResponse>.Failure(ex));
        }
    }

    public async Task<Result<RigRuntimeActionResponse>> ExecuteRigRuntimeActionAsync(RigRuntimeActionRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            bool succeeded;
            bool stateChanged;
            string message;

            _logger?.LogInformation(
                "Rig runtime action requested. Action={Action}, ForceRestart={ForceRestart}.",
                request.Action,
                request.ForceRestart);

            switch (request.Action)
            {
                case RigRuntimeActionKind.Start:
                    {
                        var result = await _rigAdapter.StartAsync(cancellationToken).ConfigureAwait(false);
                        if (result.IsFailure)
                        {
                            return Result<RigRuntimeActionResponse>.Failure(result.Error ?? new InvalidOperationException("Adapter start failed."));
                        }

                        succeeded = true;
                        stateChanged = result.Value;
                        var rigName = _rigAdapter.ActiveRig.Name;
                        message = result.Value
                            ? FormattableString.Invariant($"Adapter started for rig '{rigName}'.")
                            : FormattableString.Invariant($"Adapter already running for rig '{rigName}'.");
                        break;
                    }
                case RigRuntimeActionKind.Pause:
                    {
                        var result = await _rigAdapter.PauseAsync(cancellationToken).ConfigureAwait(false);
                        if (result.IsFailure)
                        {
                            return Result<RigRuntimeActionResponse>.Failure(result.Error ?? new InvalidOperationException("Adapter pause failed."));
                        }

                        succeeded = true;
                        stateChanged = result.Value;
                        var rigName = _rigAdapter.ActiveRig.Name;
                        message = result.Value
                            ? FormattableString.Invariant($"Adapter paused for rig '{rigName}'.")
                            : FormattableString.Invariant($"Adapter was not running for rig '{rigName}'.");
                        break;
                    }
                case RigRuntimeActionKind.Resume:
                    {
                        var result = await _rigAdapter.ResumeAsync(cancellationToken).ConfigureAwait(false);
                        if (result.IsFailure)
                        {
                            return Result<RigRuntimeActionResponse>.Failure(result.Error ?? new InvalidOperationException("Adapter resume failed."));
                        }

                        succeeded = true;
                        stateChanged = result.Value;
                        var rigName = _rigAdapter.ActiveRig.Name;
                        message = result.Value
                            ? FormattableString.Invariant($"Adapter resumed for rig '{rigName}'.")
                            : FormattableString.Invariant($"Adapter was not paused for rig '{rigName}'.");
                        break;
                    }
                case RigRuntimeActionKind.Stop:
                    {
                        var result = await _rigAdapter.StopAsync(cancellationToken).ConfigureAwait(false);
                        if (result.IsFailure)
                        {
                            return Result<RigRuntimeActionResponse>.Failure(result.Error ?? new InvalidOperationException("Adapter stop failed."));
                        }

                        succeeded = true;
                        stateChanged = result.Value;
                        var rigName = _rigAdapter.ActiveRig.Name;
                        message = result.Value
                            ? FormattableString.Invariant($"Adapter stopped for rig '{rigName}'.")
                            : FormattableString.Invariant($"Adapter already stopped for rig '{rigName}'.");
                        break;
                    }
                case RigRuntimeActionKind.Reload:
                    {
                        await _runtimeUpdater.ReloadActiveRigAsync(request.ForceRestart, cancellationToken).ConfigureAwait(false);
                        var rigName = _rigAdapter.ActiveRig.Name;
                        succeeded = true;
                        stateChanged = true;
                        message = request.ForceRestart
                            ? FormattableString.Invariant($"Reloaded rig '{rigName}' with force restart.")
                            : FormattableString.Invariant($"Reloaded rig '{rigName}'.");
                        break;
                    }
                default:
                    {
                        throw new ArgumentOutOfRangeException(nameof(request.Action), request.Action, "Unsupported adapter action.");
                    }
            }

            var status = BuildRuntimeStatus(message);
            var completedAt = _timeProvider.GetUtcNow();
            var response = new RigRuntimeActionResponse(
                request.Action,
                request.ForceRestart,
                succeeded,
                stateChanged,
                message,
                status,
                completedAt);

            _logger?.LogInformation(
                "Rig runtime action completed. Action={Action}, ForceRestart={ForceRestart}, Succeeded={Succeeded}, StateChanged={StateChanged}, State={State}.",
                request.Action,
                request.ForceRestart,
                succeeded,
                stateChanged,
                response.Status.State);

            return Result<RigRuntimeActionResponse>.Success(response);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Result<RigRuntimeActionResponse>.Failure(ex);
        }
    }

    private async Task<long> UpsertSystemSettingAsync<T>(string key, T value, long expectedRevision, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = await context.SystemSettings
            .AsTracking()
            .FirstOrDefaultAsync(s => s.Key == key, cancellationToken)
            .ConfigureAwait(false);

        var serialized = JsonSerializer.Serialize(value, JsonOptions);
        var timestamp = _timeProvider.GetUtcNow();

        if (entity is null)
        {
            if (expectedRevision > 0)
            {
                throw new InvalidOperationException($"Configuration setting '{key}' is not initialized.");
            }

            entity = new SystemSettingEntity
            {
                Key = key,
                PayloadJson = serialized,
                UpdatedUtc = timestamp,
                Revision = 1
            };

            await context.SystemSettings.AddAsync(entity, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            if (expectedRevision > 0 && expectedRevision != entity.Revision)
            {
                throw new InvalidOperationException($"Configuration setting '{key}' was updated by another request (expected revision {entity.Revision}, received {expectedRevision}).");
            }

            entity.PayloadJson = serialized;
            entity.UpdatedUtc = timestamp;
            entity.Revision = entity.Revision <= 0 ? 1 : entity.Revision + 1;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return entity.Revision;
    }

    private void InvalidateCaches(bool localApi = false, bool telemetry = false)
    {
        _snapshotInvalidator.InvalidateSnapshot();
        _observatoryCache.TryRemove(OptionsDefaults.DefaultName);

        if (localApi)
        {
            _localApiCache.TryRemove(OptionsDefaults.DefaultName);
        }

        if (telemetry)
        {
            _telemetryCache.TryRemove(OptionsDefaults.DefaultName);
        }
    }

    private RigRuntimeStatusResponse BuildRuntimeStatus(string? message)
    {
        var state = _rigAdapter.CurrentState;
        var rig = _rigAdapter.ActiveRig;
        var camera = rig.Camera;
        var descriptor = camera.Descriptor;

        var driverIdentifier = string.IsNullOrWhiteSpace(camera.DriverIdentifier)
            ? camera.DriverId.ToString()
            : camera.DriverIdentifier;

        var adapterName = string.IsNullOrWhiteSpace(descriptor.AdapterName)
            ? (string.IsNullOrWhiteSpace(descriptor.Model) ? camera.Name : descriptor.Model)
            : descriptor.AdapterName;

        var timestamp = _timeProvider.GetUtcNow();
        var detailMessage = string.IsNullOrWhiteSpace(message)
            ? FormattableString.Invariant($"Adapter state is {state}.")
            : message.Trim();

        var capabilities = CalculateCapabilities(state);

        return new RigRuntimeStatusResponse(
            state,
            capabilities,
            rig.Name,
            camera.Name,
            driverIdentifier,
            adapterName,
            timestamp,
            detailMessage);
    }

    private static RigRuntimeControlCapabilities CalculateCapabilities(RigAdapterLifecycleState state)
        => new(
            CanStart: state == RigAdapterLifecycleState.Stopped,
            CanPause: state == RigAdapterLifecycleState.Running,
            CanResume: state == RigAdapterLifecycleState.Paused,
            CanStop: state is RigAdapterLifecycleState.Running or RigAdapterLifecycleState.Paused,
            CanReload: true,
            CanForceReload: true);

    private static bool TryValidateTimeZone(string? timeZoneId, out Exception? error)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            error = new InvalidOperationException("Time zone identifier must be provided.");
            return false;
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            error = new InvalidOperationException($"Time zone '{timeZoneId}' could not be resolved on this system.", ex);
            return false;
        }
    }

    private static SystemObservatoryConfigurationResponse MapObservatory(ObservatorySiteEntity entity)
        => new()
        {
            Id = entity.Id,
            Revision = entity.Revision,
            Slug = entity.Slug,
            Name = entity.Name,
            LatitudeDegrees = entity.LatitudeDegrees,
            LongitudeDegrees = entity.LongitudeDegrees,
            TimeZoneId = entity.TimeZoneId
        };

    private static SystemLocalApiConfigurationResponse MapLocalApi(LocalApiClientOptions options, long revision)
        => new()
        {
            BaseAddress = options.BaseAddress,
            ApiKey = options.ApiKey,
            ApiKeyHeaderName = options.ApiKeyHeaderName,
            Timeout = options.Timeout,
            Revision = revision
        };

    private static SystemTelemetryRetentionConfigurationResponse MapTelemetry(SkyMonitorTelemetryRetentionOptions options, long revision)
        => new()
        {
            SweepInterval = options.SweepInterval,
            VacuumAfterPurge = options.VacuumAfterPurge,
            RemoteDispatch = MapPolicy(options.RemoteDispatch),
            FrameExports = MapPolicy(options.FrameExports),
            BackgroundStacker = MapPolicy(options.BackgroundStacker),
            CapturePacing = MapPolicy(options.CapturePacing),
            ProcessingQueue = MapPolicy(options.ProcessingQueue),
            FilterMetrics = MapPolicy(options.FilterMetrics),
            TelemetryEvents = MapPolicy(options.TelemetryEvents),
            Revision = revision
        };

    private static TelemetryRetentionPolicyModel MapPolicy(TelemetryRetentionPolicy? policy)
        => new()
        {
            MaxAgeSeconds = policy?.MaxAge?.TotalSeconds,
            MaxRecords = policy?.MaxRecords
        };

    private static TelemetryRetentionPolicy ConvertPolicy(TelemetryRetentionPolicyModel model, TelemetryRetentionPolicy fallback)
    {
        var maxAgeSeconds = model.MaxAgeSeconds;
        TimeSpan? maxAge = null;

        if (maxAgeSeconds is { } seconds && seconds > 0)
        {
            maxAge = TimeSpan.FromSeconds(Math.Min(seconds, 31536000d));
        }

        var maxRecords = model.MaxRecords ?? fallback.MaxRecords;
        if (maxRecords is not null)
        {
            maxRecords = Math.Clamp(maxRecords.Value, 1, int.MaxValue);
        }

        return TelemetryRetentionPolicy.Create(maxAge ?? fallback.MaxAge, maxRecords ?? fallback.MaxRecords);
    }

    private static LocalApiClientOptions TryDeserialize(string? payload, LocalApiClientOptions fallback)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return fallback;
        }

        try
        {
            return JsonSerializer.Deserialize<LocalApiClientOptions>(payload, JsonOptions) ?? fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    private static SkyMonitorTelemetryRetentionOptions TryDeserialize(string? payload, SkyMonitorTelemetryRetentionOptions fallback)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return fallback;
        }

        try
        {
            return JsonSerializer.Deserialize<SkyMonitorTelemetryRetentionOptions>(payload, JsonOptions) ?? fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
    }
}
