using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Components.Shared;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Services;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Exports;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.SkyMonitorV5.RPi.Components.Pages;

public sealed partial class Diagnostics : ComponentBase, IAsyncDisposable
{
    private const int HistoryCapacity = 60;
    private static readonly TimeSpan SystemRefreshInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan QueueRefreshInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan FilterRefreshInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RemoteDispatchRefreshInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FrameExportRefreshInterval = TimeSpan.FromSeconds(7);
    private static readonly TimeSpan LogsRefreshInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StorageRefreshInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BackgroundRefreshInterval = TimeSpan.FromSeconds(5);
    private const string DefaultTabKey = "overview";
    private const string DiagnosticsApiBasePath = "/api/v1.0/diagnostics";

    private readonly List<double> _queueFillHistory = new();
    private readonly List<double> _queueLatencyHistory = new();
    private readonly List<double> _stackDurationHistory = new();
    private readonly List<double> _remoteDispatchLatencyHistory = new();
    private readonly List<double> _frameExportLatencyHistory = new();
    private readonly List<double> _frameExportQueueLatencyHistory = new();
    private readonly List<double> _frameExportProcessingHistory = new();
    private readonly List<double> _frameExportFullPipelineHistory = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly List<TelemetryEventLogEntry> _telemetryEvents = new();
    private readonly HashSet<long> _telemetryEventIds = new();

    private CancellationTokenSource? _refreshCts;
    private Task? _refreshTask;
    private BackgroundStackerMetricsResponse? _stackerMetrics;
    private FilterMetricsSnapshot? _filterMetrics;
    private SystemDiagnosticsSnapshot? _systemDiagnostics;
    private RemoteDispatchMetricsSnapshot? _remoteDispatchMetrics;
    private RemoteDispatchHistorySample? _lastRemoteDispatchSample;
    private FrameExportMetricsSnapshot? _frameExportMetrics;
    private FrameExportHistorySample? _lastFrameExportSample;
    private IReadOnlyList<FrameExportHistorySample> _frameExportHistory = Array.Empty<FrameExportHistorySample>();
    private DataStoreMetricsSnapshot? _dataStoreMetrics;
    private long? _latestTelemetryEventId;
    private long? _oldestTelemetryEventId;
    private bool _logsInitialised;
    private bool _logsHasOlder;
    private bool _logsHasNewer;
    private DateTimeOffset? _lastUpdated;
    private string? _errorMessage;
    private bool _isLoading = true;
    private DiagnosticsTab _activeTab = DiagnosticsTab.Overview;
    private string _activeTabKey = DefaultTabKey;
    private DateTimeOffset _lastSystemRefreshUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastQueueRefreshUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastFilterRefreshUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastRemoteDispatchRefreshUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastFrameExportRefreshUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastLogsRefreshUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastStorageRefreshUtc = DateTimeOffset.MinValue;
    private RemoteDispatchConfigSnapshot _remoteDispatchConfig = RemoteDispatchConfigSnapshot.Disabled;
    private IDisposable? _optionsChangeSubscription;

    [Parameter]
    [SupplyParameterFromQuery(Name = "tab")]
    public string? RequestedTabKey { get; set; }

    [Inject]
    private IDiagnosticsService DiagnosticsService { get; set; } = default!;

    [Inject]
    private ILogger<Diagnostics> Logger { get; set; } = default!;

    [Inject]
    private IObservatoryClock ObservatoryClock { get; set; } = default!;

    [Inject]
    private IOptionsMonitor<CameraPipelineOptions> CameraPipelineOptionsMonitor { get; set; } = default!;

    private bool IsLoading => _isLoading;
    private BackgroundStackerMetricsResponse? StackerMetrics => _stackerMetrics;
    private FilterMetricsSnapshot? FilterMetricsSnapshot => _filterMetrics;
    private string? ErrorMessage => _errorMessage;
    private string? LastUpdatedDisplay => _lastUpdated?.ToString("HH:mm:ss", CultureInfo.CurrentCulture);
    private string RefreshIntervalDisplay => string.Create(CultureInfo.CurrentCulture, $"{GetCurrentLoopInterval().TotalSeconds:F1} s");
    private string AutoRefreshStatus => _refreshCts is { IsCancellationRequested: false } ? $"{ActiveTab} metrics" : "Paused";
    private string DiagnosticsHealthDisplay => string.IsNullOrEmpty(_errorMessage) ? "Nominal" : "Needs attention";
    private DiagnosticsTab ActiveTab => _activeTab;
    private IReadOnlyList<SkyMonitorTabDefinition> DiagnosticsTabs => SkyMonitorTabCatalog.DiagnosticsTabs;
    private string ActiveTabKey => _activeTabKey;
    private string QueueFillGaugeStyle => BuildGaugeStyle(_stackerMetrics?.QueueFillPercentage ?? 0);
    private string QueueFillPercentageDisplay => _stackerMetrics is { } metrics ? FormatPercent(metrics.QueueFillPercentage) : "—";
    private string QueueDepthSummary => _stackerMetrics is { } metrics ? FormatDepth(metrics.QueueDepth, metrics.QueueCapacity) : "—";
    private string PeakQueueDepthSummary => _stackerMetrics is { } metrics ? FormatCount(metrics.PeakQueueDepth) : "—";
    private string PeakQueueFillDisplay => _stackerMetrics is { } metrics ? FormatPercent(metrics.PeakQueueFillPercentage) : "—";
    private string QueuePressureDisplay => _stackerMetrics is { } metrics ? DescribeQueuePressure(metrics.QueuePressureLevel) : "—";
    private string SecondsSinceLastCompletedDisplay => _stackerMetrics switch
    {
        { SecondsSinceLastCompleted: { } seconds } => FormatSeconds((double?)seconds),
        { ProcessedFrameCount: > 0 } => FormatSeconds(0d),
        _ => "No frames yet"
    };
    private string ProcessedFrameCountDisplay => _stackerMetrics is { } metrics ? FormatCount(metrics.ProcessedFrameCount) : "—";
    private string DroppedFrameCountDisplay => _stackerMetrics is { } metrics ? FormatCount(metrics.DroppedFrameCount) : "—";
    private string QueueMemorySummary => _stackerMetrics is { } metrics ? FormatMemory(metrics.QueueMemoryMegabytes) : "—";
    private string PeakQueueMemorySummary => _stackerMetrics is { } metrics ? FormatMemory(metrics.PeakQueueMemoryMegabytes) : "—";
    private string LastFrameNumberDisplay => _stackerMetrics?.LastFrameNumber?.ToString("N0", CultureInfo.CurrentCulture) ?? "—";
    private string HistoryDurationDisplay => BuildHistoryDurationLabel(_queueFillHistory.Count, QueueRefreshInterval);
    private string LatencyMaxDisplay => BuildMaxLabel(_queueLatencyHistory, "ms");
    private string StackDurationMaxDisplay => BuildMaxLabel(_stackDurationHistory, "ms");
    private string FilterSummaryDisplay => _filterMetrics is { Filters.Count: > 0 }
        ? $"{_filterMetrics.Filters.Count} active filters"
        : "No filter telemetry yet";

    private RemoteDispatchMetricsSnapshot? RemoteDispatchMetrics => _remoteDispatchMetrics;
    private IReadOnlyList<RemoteDispatchFormatSummary> RemoteDispatchFormatSummaries => RemoteDispatchMetrics?.FormatCounts ?? Array.Empty<RemoteDispatchFormatSummary>();
    private bool HasRemoteDispatchTelemetry => RemoteDispatchMetrics is { SampleCount: > 0 };
    private string RemoteDispatchSampleCountDisplay => HasRemoteDispatchTelemetry ? FormatCount(RemoteDispatchMetrics!.SampleCount) : "0";
    private string RemoteDispatchSuccessRateDisplay => HasRemoteDispatchTelemetry ? FormatPercent(RemoteDispatchMetrics!.SuccessRatePercent) : "—";
    private string RemoteDispatchOutcomeSummary => HasRemoteDispatchTelemetry
        ? $"{FormatCount(RemoteDispatchMetrics!.SuccessCount)} ok · {FormatCount(RemoteDispatchMetrics.FailureCount)} failed · {FormatCount(RemoteDispatchMetrics.SkippedCount)} skipped"
        : "No attempts yet";
    private string RemoteDispatchAverageLatencyDisplay => FormatMilliseconds(RemoteDispatchMetrics?.AverageLatencyMilliseconds);
    private string RemoteDispatchPeakLatencyDisplay => FormatMilliseconds(RemoteDispatchMetrics?.PeakLatencyMilliseconds);
    private string RemoteDispatchLastLatencyDisplay => FormatMilliseconds(RemoteDispatchMetrics?.LastLatencyMilliseconds);
    private string RemoteDispatchLastPayloadDisplay => BuildRemoteDispatchPayloadDescriptor();
    private string RemoteDispatchLastOutcomeDisplay => _lastRemoteDispatchSample is { } sample
        ? $"{FormatRemoteDispatchOutcome(sample.Outcome)} ({sample.Mode})"
        : "No attempts yet";
    private string RemoteDispatchLastAttemptDisplay => _lastRemoteDispatchSample is { Timestamp: var ts }
        ? ts.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture)
        : "—";
    private string RemoteDispatchLastMessageDisplay => _lastRemoteDispatchSample switch
    {
        { ErrorMessage: { Length: > 0 } error } => error,
        { Message: { Length: > 0 } message } => message,
        _ => "—"
    };
    private string RemoteDispatchHistoryDurationDisplay => BuildHistoryDurationLabel(_remoteDispatchLatencyHistory.Count, RemoteDispatchRefreshInterval);
    private string RemoteDispatchLatencyMaxDisplay => BuildMaxLabel(_remoteDispatchLatencyHistory, "ms");
    private FrameExportMetricsSnapshot? FrameExportMetrics => _frameExportMetrics;
    private IReadOnlyList<FrameExportSinkMetrics> FrameExportSinks => FrameExportMetrics?.Sinks ?? Array.Empty<FrameExportSinkMetrics>();
    private IReadOnlyList<FrameExportHistorySample> FrameExportHistory => _frameExportHistory;
    private bool HasFrameExportTelemetry => FrameExportMetrics is { TotalAttemptCount: > 0 };
    private string FrameExportAttemptCountDisplay => FormatCount(FrameExportMetrics?.TotalAttemptCount ?? 0);
    private string FrameExportSuccessCountDisplay => FormatCount(FrameExportMetrics?.TotalSuccessCount ?? 0);
    private string FrameExportFailureCountDisplay => FormatCount(FrameExportMetrics?.TotalFailureCount ?? 0);
    private string FrameExportSuccessRateDisplay => FormatPercent(FrameExportMetrics?.SuccessRatePercent);
    private string FrameExportPendingRetryDisplay => FormatCount(FrameExportMetrics?.PendingRetryCount ?? 0);
    private IReadOnlyList<FrameExportRetryEntry> FrameExportPendingRetries => FrameExportMetrics?.PendingRetries ?? Array.Empty<FrameExportRetryEntry>();
    private bool HasFrameExportPendingRetries => FrameExportPendingRetries.Count > 0;
    private bool HasAdditionalPendingRetries => (FrameExportMetrics?.PendingRetryCount ?? 0) > FrameExportPendingRetries.Count;
    private string FrameExportHistoryDurationDisplay => BuildHistoryDurationLabel(_frameExportLatencyHistory.Count, FrameExportRefreshInterval);
    private string FrameExportLatencyMaxDisplay => BuildMaxLabel(_frameExportLatencyHistory, "ms");
    private string FrameExportQueueLatencyMaxDisplay => BuildMaxLabel(_frameExportQueueLatencyHistory, "ms");
    private string FrameExportProcessingMaxDisplay => BuildMaxLabel(_frameExportProcessingHistory, "ms");
    private string FrameExportFullPipelineMaxDisplay => BuildMaxLabel(_frameExportFullPipelineHistory, "ms");
    private string FrameExportLastOutcomeDisplay => _lastFrameExportSample is { } sample ? FormatFrameExportAttemptOutcome(sample.Success) : "No attempts yet";
    private string FrameExportLastAttemptDisplay => _lastFrameExportSample is { } sample
        ? sample.AttemptedAtLocal.ToString("HH:mm:ss", CultureInfo.CurrentCulture)
        : "—";
    private string FrameExportLastLatencyDisplay => FormatMilliseconds(_lastFrameExportSample?.LatencyMilliseconds);
    private string FrameExportLastQueueLatencyDisplay => FormatMilliseconds(_lastFrameExportSample?.QueueLatencyMilliseconds);
    private string FrameExportLastProcessingDisplay => FormatMilliseconds(_lastFrameExportSample?.ProcessingMilliseconds);
    private string FrameExportLastFullPipelineDisplay => FormatMilliseconds(_lastFrameExportSample?.FullPipelineMilliseconds);
    private string FrameExportLastPayloadDisplay => BuildFrameExportPayloadDescriptor(_lastFrameExportSample);
    private IEnumerable<FrameExportHistorySample> FrameExportHistoryDescending => FrameExportHistory.Count > 0
        ? FrameExportHistory.Reverse().Take(10)
        : Array.Empty<FrameExportHistorySample>();
    private bool HasFrameExportHistory => FrameExportHistory.Count > 0;
    private IEnumerable<FrameExportSinkMetrics> FrameExportSinksByAttempts => FrameExportSinks.Count > 0
        ? FrameExportSinks.OrderByDescending(static sink => sink.AttemptCount)
        : Array.Empty<FrameExportSinkMetrics>();
    private DataStoreMetricsSnapshot? DataStoreMetrics => _dataStoreMetrics;
    private DataStoreInstanceMetrics? TelemetryStoreMetrics => DataStoreMetrics?.TelemetryStore;
    private DataStoreInstanceMetrics? ConfigurationStoreMetrics => DataStoreMetrics?.ConfigurationStore;
    private bool HasDataStoreMetrics => DataStoreMetrics is not null;
    private string TelemetryDatabaseSizeDisplay => FormatBytes(TelemetryStoreMetrics?.FileBytes);
    private string TelemetryDatabasePagesDisplay => FormatCount(TelemetryStoreMetrics?.PageCount ?? 0);
    private string TelemetryDatabaseFreePagesDisplay => FormatCount(TelemetryStoreMetrics?.FreePages ?? 0);
    private string TelemetryRetentionLastRunDisplay => FormatTimestamp(TelemetryStoreMetrics?.TelemetryRetention?.LastCompletedAtUtc);
    private string ConfigurationDatabaseSizeDisplay => FormatBytes(ConfigurationStoreMetrics?.FileBytes);
    private string TelemetryQueueDepthDisplay => TelemetryStoreMetrics?.TelemetryIngestion is { } metrics ? metrics.QueueDepth.ToString("N0", CultureInfo.CurrentCulture) : "—";
    private string TelemetryIngestionLatencyDisplay => TelemetryStoreMetrics?.TelemetryIngestion is { } metrics ? FormatMilliseconds(metrics.LastIngestionLatencyMilliseconds) : "—";
    private IReadOnlyList<DataStoreTableMetric> TelemetryTables => TelemetryStoreMetrics?.Tables ?? Array.Empty<DataStoreTableMetric>();
    private IReadOnlyList<DataStoreTableMetric> ConfigurationTables => ConfigurationStoreMetrics?.Tables ?? Array.Empty<DataStoreTableMetric>();
    private TelemetryRetentionSummaryMetrics? TelemetryRetentionMetrics => TelemetryStoreMetrics?.TelemetryRetention;
    private DataStoreBootstrapStatusMetrics? TelemetryBootstrap => TelemetryStoreMetrics?.Bootstrap;
    private DataStoreBootstrapStatusMetrics? ConfigurationBootstrap => ConfigurationStoreMetrics?.Bootstrap;
    private IReadOnlyList<TelemetryEventLogEntry> TelemetryEvents => _telemetryEvents;
    private bool HasTelemetryEvents => _telemetryEvents.Count > 0;
    private string TelemetryEventCountDisplay => _telemetryEvents.Count.ToString("N0", CultureInfo.CurrentCulture);
    private bool LogsHasOlder => _logsHasOlder;
    private bool LogsHasNewer => _logsHasNewer;
    private string LogsStatusDisplay => _logsInitialised ? $"Streaming · {TelemetryEventCountDisplay} events" : "Not loaded";
    private string RemoteDispatchConfigSummary => _remoteDispatchConfig.Summary;
    private string RemoteDispatchConfigBadgeText => _remoteDispatchConfig.Status switch
    {
        RemoteDispatchConfigurationStatus.Enabled => "Enabled",
        RemoteDispatchConfigurationStatus.Warning => "Needs setup",
        _ => "Disabled"
    };
    private string RemoteDispatchConfigBadgeCss => _remoteDispatchConfig.Status switch
    {
        RemoteDispatchConfigurationStatus.Enabled => "badge badge-status badge-status--enabled",
        RemoteDispatchConfigurationStatus.Warning => "badge badge-status badge-status--warning",
        _ => "badge badge-status badge-status--disabled"
    };
    private string RemoteDispatchModeDisplay => _remoteDispatchConfig.Mode switch
    {
        RemoteDispatchMode.S3 => "S3 / MinIO",
        RemoteDispatchMode.None => "Disabled",
        _ => _remoteDispatchConfig.Mode.ToString()
    };
    private bool ShouldShowRemoteDispatchConnection => _remoteDispatchConfig.Mode is RemoteDispatchMode.S3;
    private string RemoteDispatchBucketDisplay => string.IsNullOrWhiteSpace(_remoteDispatchConfig.Bucket) ? "—" : _remoteDispatchConfig.Bucket!;
    private string RemoteDispatchEndpointDisplay => string.IsNullOrWhiteSpace(_remoteDispatchConfig.Endpoint) ? "—" : _remoteDispatchConfig.Endpoint!;
    private string RemoteDispatchProtocolDisplay => _remoteDispatchConfig.UseSsl ? "HTTPS" : "HTTP";
    private string RemoteDispatchFormatDisplay => _remoteDispatchConfig.ImageFormat.ToString().ToUpperInvariant();
    private bool HasRemoteDispatchConfigIssues => _remoteDispatchConfig.Issues.Count > 0;
    private IReadOnlyList<string> RemoteDispatchConfigIssues => _remoteDispatchConfig.Issues;

    private SystemDiagnosticsSnapshot? SystemDiagnostics => _systemDiagnostics;
    private IReadOnlyList<CoreCpuLoad> CoreCpuLoads => _systemDiagnostics?.CoreCpuLoads ?? Array.Empty<CoreCpuLoad>();
    private bool HasCoreCpuLoads => CoreCpuLoads.Count > 0;
    private double TotalCpuGaugeValue => _systemDiagnostics switch
    {
        { TotalCpuPercent: { } total } => total,
        { } metrics => metrics.ProcessCpuPercent,
        _ => 0d
    };
    private string TotalCpuGaugeStyle => BuildGaugeStyle(TotalCpuGaugeValue);
    private string TotalCpuDisplay => _systemDiagnostics switch
    {
        { TotalCpuPercent: { } total } => FormatPercent(total),
        { } metrics => FormatPercent(metrics.ProcessCpuPercent),
        _ => "—"
    };
    private string TotalCpuGaugeLabel => _systemDiagnostics?.TotalCpuPercent.HasValue == true ? "overall" : "process";
    private string ProcessCpuDisplay => _systemDiagnostics is { } metrics ? FormatPercent(metrics.ProcessCpuPercent) : "—";
    private string ProcessThreadsDisplay => _systemDiagnostics is { ThreadCount: var threads } ? threads.ToString("N0", CultureInfo.CurrentCulture) : "—";
    private string ProcessUptimeDisplay => FormatDuration(_systemDiagnostics?.UptimeSeconds);
    private string MemoryUsageDisplay => _systemDiagnostics is { Memory.UsagePercent: { } percent } ? FormatPercent(percent) : "—";
    private string MemoryUsageGaugeStyle => BuildGaugeStyle(_systemDiagnostics?.Memory.UsagePercent ?? 0d);
    private string SystemMemoryTotalDisplay => FormatMegabytes(_systemDiagnostics?.Memory.TotalMegabytes);
    private string SystemMemoryUsedDisplay => FormatMegabytes(_systemDiagnostics?.Memory.UsedMegabytes);
    private string SystemMemoryFreeDisplay => FormatMegabytes(_systemDiagnostics?.Memory.FreeMegabytes);
    private string SystemMemoryAvailableDisplay => FormatMegabytes(_systemDiagnostics?.Memory.AvailableMegabytes);
    private string SystemMemoryCachedDisplay => FormatMegabytes(_systemDiagnostics?.Memory.CachedMegabytes);
    private string SystemMemoryBuffersDisplay => FormatMegabytes(_systemDiagnostics?.Memory.BuffersMegabytes);
    private string ProcessWorkingSetDisplay => FormatMemory(_systemDiagnostics?.ProcessWorkingSetMegabytes ?? double.NaN);
    private string ProcessPrivateDisplay => FormatMemory(_systemDiagnostics?.ProcessPrivateMegabytes ?? double.NaN);
    private string ManagedMemoryDisplay => FormatMemory(_systemDiagnostics?.ManagedMemoryMegabytes ?? double.NaN);

    private static string FormatRetryFrameId(Guid frameId)
    {
        var text = frameId.ToString("N", CultureInfo.InvariantCulture);
        return text.Length <= 8 ? text.ToUpperInvariant() : text[..8].ToUpperInvariant();
    }

    private static string FormatRetryTimestamp(DateTimeOffset timestamp)
        => timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);

    private static string FormatRetryTimestamp(DateTimeOffset? timestamp)
        => timestamp.HasValue ? FormatRetryTimestamp(timestamp.Value) : "—";

    private static string FormatRetryError(string? message)
        => string.IsNullOrWhiteSpace(message) ? "—" : message!;

    private static string FormatRetryTooltip(DateTimeOffset timestamp)
        => timestamp.ToLocalTime().ToString("G", CultureInfo.CurrentCulture);

    private string ResolveActiveTabKey(string? requestedKey)
    {
        if (string.IsNullOrWhiteSpace(requestedKey))
        {
            return DefaultTabKey;
        }

        foreach (var tab in DiagnosticsTabs)
        {
            if (string.Equals(tab.Key, requestedKey, StringComparison.OrdinalIgnoreCase))
            {
                return tab.Key;
            }
        }

        return DefaultTabKey;
    }

    private static DiagnosticsTab ResolveDiagnosticsTab(string tabKey)
        => tabKey switch
        {
            "pipeline" => DiagnosticsTab.Pipeline,
            "filters" => DiagnosticsTab.Pipeline,
            "queue" => DiagnosticsTab.Pipeline,
            "dispatch" => DiagnosticsTab.Dispatch,
            "exports" => DiagnosticsTab.Exports,
            "logs" => DiagnosticsTab.Logs,
            "storage" => DiagnosticsTab.Storage,
            "system" => DiagnosticsTab.Overview,
            _ => DiagnosticsTab.Overview
        };

    protected override void OnParametersSet()
    {
        var resolvedKey = ResolveActiveTabKey(RequestedTabKey);
        var resolvedTab = ResolveDiagnosticsTab(resolvedKey);

        var keyChanged = !string.Equals(resolvedKey, _activeTabKey, StringComparison.OrdinalIgnoreCase);
        var tabChanged = _activeTab != resolvedTab;

        if (keyChanged)
        {
            _activeTabKey = resolvedKey;
        }

        if (tabChanged)
        {
            _activeTab = resolvedTab;
            ForceRefreshForTab(_activeTab);

            if (_refreshCts is { } cts)
            {
                var token = cts.Token;
                _ = InvokeAsync(async () => await RefreshAsync(token).ConfigureAwait(false));
            }
        }
    }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();

        _refreshCts = new CancellationTokenSource();
        ForceRefreshForTab(_activeTab);

        UpdateRemoteDispatchConfig(CameraPipelineOptionsMonitor.CurrentValue);
        _optionsChangeSubscription = CameraPipelineOptionsMonitor.OnChange(options =>
        {
            UpdateRemoteDispatchConfig(options);
            _ = InvokeAsync(StateHasChanged);
        });

        var token = _refreshCts.Token;
        await RefreshAsync(token).ConfigureAwait(false);
        _refreshTask = RunRefreshLoopAsync(token);
    }

    private async Task RefreshNowAsync()
    {
        if (_refreshCts is null)
        {
            return;
        }

        ForceRefreshForTab(_activeTab);
        await RefreshAsync(_refreshCts.Token).ConfigureAwait(false);
    }

    private async Task LoadOlderTelemetryEventsAsync()
    {
        if (!_logsHasOlder || !_oldestTelemetryEventId.HasValue)
        {
            return;
        }

        var cancellationToken = _refreshCts?.Token ?? CancellationToken.None;

        try
        {
            await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            var result = await DiagnosticsService.GetTelemetryEventsAsync(beforeId: _oldestTelemetryEventId, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (result.IsSuccessful)
            {
                ApplyTelemetryEventsPage(result.Value, TelemetryEventLoadMode.Older);

                if (result.Value.OldestEventId.HasValue)
                {
                    _oldestTelemetryEventId = result.Value.OldestEventId;
                }

                if (result.Value.LatestEventId.HasValue && !_latestTelemetryEventId.HasValue)
                {
                    _latestTelemetryEventId = result.Value.LatestEventId;
                }

                _logsHasOlder = result.Value.HasMoreBefore;
                _logsHasNewer = _logsHasNewer || result.Value.HasMoreAfter;
                _logsInitialised = true;
            }
            else
            {
                var error = result.Error ?? new InvalidOperationException("Unknown telemetry events error");
                Logger.LogWarning(error, "Failed to load older diagnostic telemetry events.");
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unexpected error while loading older telemetry events.");
        }
        finally
        {
            _refreshLock.Release();
        }

        await InvokeAsync(StateHasChanged);
    }

    private void ForceRefreshForTab(DiagnosticsTab tab)
    {
        switch (tab)
        {
            case DiagnosticsTab.Overview:
                _lastSystemRefreshUtc = DateTimeOffset.MinValue;
                break;
            case DiagnosticsTab.Pipeline:
                _lastQueueRefreshUtc = DateTimeOffset.MinValue;
                _lastFilterRefreshUtc = DateTimeOffset.MinValue;
                break;
            case DiagnosticsTab.Dispatch:
                _lastRemoteDispatchRefreshUtc = DateTimeOffset.MinValue;
                break;
            case DiagnosticsTab.Exports:
                _lastFrameExportRefreshUtc = DateTimeOffset.MinValue;
                break;
            case DiagnosticsTab.Logs:
                _lastLogsRefreshUtc = DateTimeOffset.MinValue;
                break;
            case DiagnosticsTab.Storage:
                _lastStorageRefreshUtc = DateTimeOffset.MinValue;
                break;
        }
    }

    private TimeSpan GetCurrentLoopInterval() => _activeTab switch
    {
        DiagnosticsTab.Overview => SystemRefreshInterval,
        DiagnosticsTab.Pipeline => QueueRefreshInterval,
        DiagnosticsTab.Dispatch => RemoteDispatchRefreshInterval,
        DiagnosticsTab.Exports => FrameExportRefreshInterval,
        DiagnosticsTab.Logs => LogsRefreshInterval,
        DiagnosticsTab.Storage => StorageRefreshInterval,
        _ => BackgroundRefreshInterval
    };

    public async ValueTask DisposeAsync()
    {
        if (_refreshCts is not null)
        {
            try
            {
                _refreshCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed.
            }
        }

        if (_refreshTask is not null)
        {
            try
            {
                await _refreshTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Diagnostics refresh loop ended with an error during disposal.");
            }
        }

        _refreshCts?.Dispose();
        _optionsChangeSubscription?.Dispose();

        _refreshLock.Dispose();
    }

    private async Task RunRefreshLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var delay = GetCurrentLoopInterval();
                var delayCompleted = await CancellationTokenHelpers.DelayWithoutThrowAsync(delay, cancellationToken).ConfigureAwait(false);
                if (!delayCompleted)
                {
                    break;
                }

                try
                {
                    await RefreshAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Diagnostics refresh loop encountered an unexpected error.");
            _errorMessage = "Diagnostics refresh loop encountered an unexpected error. Check logs for details.";
            await InvokeAsync(StateHasChanged);
        }
    }

    private void UpdateRemoteDispatchConfig(CameraPipelineOptions? pipelineOptions)
    {
        if (pipelineOptions is null)
        {
            _remoteDispatchConfig = RemoteDispatchConfigSnapshot.Disabled;
            return;
        }

        var options = pipelineOptions.RemoteDispatch ?? new RemoteDispatchOptions();

        if (!options.Enabled || options.Mode is RemoteDispatchMode.None)
        {
            _remoteDispatchConfig = RemoteDispatchConfigSnapshot.Disabled with
            {
                ImageFormat = options.ImageFormat
            };
            return;
        }

        if (options.Mode is not RemoteDispatchMode.S3)
        {
            _remoteDispatchConfig = new RemoteDispatchConfigSnapshot(
                RemoteDispatchConfigurationStatus.Warning,
                options.Mode,
                "Remote dispatch is enabled but uses an unsupported mode.",
                null,
                null,
                options.ImageFormat,
                options.UseSsl,
                new[] { $"Mode '{options.Mode}' is not implemented. Disable remote dispatch or switch to S3." });
            return;
        }

        var issues = new List<string>();

        if (string.IsNullOrWhiteSpace(options.S3Bucket))
        {
            issues.Add("Bucket name is required when remote dispatch is enabled.");
        }

        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            issues.Add("Endpoint must be specified (for example, https://minio:9000).");
        }

        if (string.IsNullOrWhiteSpace(options.AccessKey) || string.IsNullOrWhiteSpace(options.SecretKey))
        {
            issues.Add("Access key and secret key must be provided.");
        }

        if (issues.Count > 0)
        {
            _remoteDispatchConfig = new RemoteDispatchConfigSnapshot(
                RemoteDispatchConfigurationStatus.Warning,
                options.Mode,
                "Remote dispatch is enabled but configuration needs attention.",
                options.S3Bucket,
                options.Endpoint,
                options.ImageFormat,
                options.UseSsl,
                issues);
            return;
        }

        var protocol = options.UseSsl ? "HTTPS" : "HTTP";
        var summary = $"Publishing enabled via {protocol} S3.";

        _remoteDispatchConfig = new RemoteDispatchConfigSnapshot(
            RemoteDispatchConfigurationStatus.Enabled,
            options.Mode,
            summary,
            options.S3Bucket,
            options.Endpoint,
            options.ImageFormat,
            options.UseSsl,
            Array.Empty<string>());
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken);

        try
        {
            var errorMessages = new List<string>();
            BackgroundStackerMetricsResponse? latestQueueMetrics = null;
            var queueHistoryApplied = false;
            RemoteDispatchMetricsSnapshot? latestRemoteDispatchMetrics = null;
            var remoteHistoryApplied = false;
            FrameExportMetricsSnapshot? latestFrameExportMetrics = null;
            var frameExportHistoryApplied = false;
            var nowUtc = ObservatoryClock.UtcNow;

            if (ShouldRefreshQueueMetrics() && nowUtc - _lastQueueRefreshUtc >= QueueRefreshInterval)
            {
                try
                {
                    var stackerResult = await DiagnosticsService.GetBackgroundStackerMetricsAsync(cancellationToken).ConfigureAwait(false);
                    if (stackerResult.IsSuccessful)
                    {
                        var metrics = stackerResult.Value;
                        latestQueueMetrics = metrics;
                        _stackerMetrics = metrics;
                        _lastUpdated = ObservatoryClock.LocalNow;
                    }
                    else
                    {
                        var error = stackerResult.Error ?? new InvalidOperationException("Unknown diagnostics error");
                        Logger.LogWarning(error, "Failed to refresh background stacker metrics.");
                        errorMessages.Add("Unable to retrieve background stacker metrics.");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Unexpected error refreshing background stacker metrics.");
                    errorMessages.Add("Unexpected error while retrieving background stacker metrics.");
                }

                try
                {
                    var historyResult = await DiagnosticsService.GetBackgroundStackerHistoryAsync(cancellationToken).ConfigureAwait(false);
                    if (historyResult.IsSuccessful)
                    {
                        ApplyHistory(historyResult.Value.Samples);
                        queueHistoryApplied = true;
                    }
                    else
                    {
                        var error = historyResult.Error ?? new InvalidOperationException("Unknown diagnostics history error");
                        Logger.LogWarning(error, "Failed to refresh background stacker history samples.");
                        errorMessages.Add("Unable to retrieve background stacker history.");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Unexpected error refreshing background stacker history.");
                    errorMessages.Add("Unexpected error while retrieving background stacker history.");
                }

                if (!queueHistoryApplied && latestQueueMetrics is not null)
                {
                    UpdateHistory(_queueFillHistory, latestQueueMetrics.QueueFillPercentage);
                    UpdateHistory(_queueLatencyHistory, latestQueueMetrics.LastQueueLatencyMilliseconds ?? 0d);
                    UpdateHistory(_stackDurationHistory, latestQueueMetrics.LastStackMilliseconds ?? 0d);
                }

                _lastQueueRefreshUtc = nowUtc;
            }

            if (ShouldRefreshRemoteDispatchMetrics() && nowUtc - _lastRemoteDispatchRefreshUtc >= RemoteDispatchRefreshInterval)
            {
                try
                {
                    var remoteResult = await DiagnosticsService.GetRemoteDispatchMetricsAsync(cancellationToken).ConfigureAwait(false);
                    if (remoteResult.IsSuccessful)
                    {
                        latestRemoteDispatchMetrics = remoteResult.Value;
                        _remoteDispatchMetrics = remoteResult.Value;
                    }
                    else
                    {
                        var error = remoteResult.Error ?? new InvalidOperationException("Unknown remote dispatch diagnostics error");
                        Logger.LogWarning(error, "Failed to refresh remote dispatch metrics snapshot.");
                        errorMessages.Add("Unable to retrieve remote dispatch metrics.");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Unexpected error refreshing remote dispatch metrics snapshot.");
                    errorMessages.Add("Unexpected error while retrieving remote dispatch metrics.");
                }

                try
                {
                    var remoteHistoryResult = await DiagnosticsService.GetRemoteDispatchHistoryAsync(cancellationToken).ConfigureAwait(false);
                    if (remoteHistoryResult.IsSuccessful)
                    {
                        ApplyRemoteDispatchHistory(remoteHistoryResult.Value.Samples);
                        remoteHistoryApplied = true;
                    }
                    else
                    {
                        var error = remoteHistoryResult.Error ?? new InvalidOperationException("Unknown remote dispatch history error");
                        Logger.LogWarning(error, "Failed to refresh remote dispatch history samples.");
                        errorMessages.Add("Unable to retrieve remote dispatch history.");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Unexpected error refreshing remote dispatch history.");
                    errorMessages.Add("Unexpected error while retrieving remote dispatch history.");
                }

                if (!remoteHistoryApplied && latestRemoteDispatchMetrics is { LastLatencyMilliseconds: { } lastLatency })
                {
                    UpdateHistory(_remoteDispatchLatencyHistory, lastLatency);
                }

                _lastRemoteDispatchRefreshUtc = nowUtc;
            }

            if (ShouldRefreshFrameExportMetrics() && nowUtc - _lastFrameExportRefreshUtc >= FrameExportRefreshInterval)
            {
                try
                {
                    var exportResult = await DiagnosticsService.GetFrameExportMetricsAsync(cancellationToken).ConfigureAwait(false);
                    if (exportResult.IsSuccessful)
                    {
                        latestFrameExportMetrics = exportResult.Value;
                        _frameExportMetrics = exportResult.Value;
                        _lastUpdated = ObservatoryClock.LocalNow;
                    }
                    else
                    {
                        var error = exportResult.Error ?? new InvalidOperationException("Unknown frame export diagnostics error");
                        Logger.LogWarning(error, "Failed to refresh frame export metrics snapshot.");
                        errorMessages.Add("Unable to retrieve frame export metrics.");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Unexpected error refreshing frame export metrics snapshot.");
                    errorMessages.Add("Unexpected error while retrieving frame export metrics.");
                }

                try
                {
                    var exportHistoryResult = await DiagnosticsService.GetFrameExportHistoryAsync(cancellationToken).ConfigureAwait(false);
                    if (exportHistoryResult.IsSuccessful)
                    {
                        ApplyFrameExportHistory(exportHistoryResult.Value.Attempts);
                        frameExportHistoryApplied = true;
                    }
                    else
                    {
                        var error = exportHistoryResult.Error ?? new InvalidOperationException("Unknown frame export history error");
                        Logger.LogWarning(error, "Failed to refresh frame export history samples.");
                        errorMessages.Add("Unable to retrieve frame export history.");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Unexpected error refreshing frame export history.");
                    errorMessages.Add("Unexpected error while retrieving frame export history.");
                }

                if (!frameExportHistoryApplied && latestFrameExportMetrics is not null)
                {
                    ApplyFrameExportFallbackFromMetrics(latestFrameExportMetrics);
                }

                _lastFrameExportRefreshUtc = nowUtc;
            }

            if (ShouldRefreshFilterMetrics() && nowUtc - _lastFilterRefreshUtc >= FilterRefreshInterval)
            {
                try
                {
                    var filterResult = await DiagnosticsService.GetFilterMetricsAsync(cancellationToken).ConfigureAwait(false);
                    if (filterResult.IsSuccessful)
                    {
                        _filterMetrics = filterResult.Value;
                    }
                    else
                    {
                        var error = filterResult.Error ?? new InvalidOperationException("Unknown diagnostics error");
                        Logger.LogWarning(error, "Failed to refresh filter metrics.");
                        errorMessages.Add("Unable to retrieve filter telemetry.");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Unexpected error refreshing filter metrics.");
                    errorMessages.Add("Unexpected error while retrieving filter telemetry.");
                }

                _lastFilterRefreshUtc = nowUtc;
            }

            if (ShouldRefreshSystemMetrics() && nowUtc - _lastSystemRefreshUtc >= SystemRefreshInterval)
            {
                try
                {
                    var systemResult = await DiagnosticsService.GetSystemDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
                    if (systemResult.IsSuccessful)
                    {
                        _systemDiagnostics = systemResult.Value;
                        _lastUpdated = ObservatoryClock.LocalNow;
                    }
                    else
                    {
                        var error = systemResult.Error ?? new InvalidOperationException("Unknown system diagnostics error");
                        Logger.LogWarning(error, "Failed to refresh system diagnostics snapshot.");
                        errorMessages.Add("Unable to retrieve system diagnostics.");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Unexpected error refreshing system diagnostics snapshot.");
                    errorMessages.Add("Unexpected error while retrieving system diagnostics.");
                }

                _lastSystemRefreshUtc = nowUtc;
            }

            if (ShouldRefreshLogs() && nowUtc - _lastLogsRefreshUtc >= LogsRefreshInterval)
            {
                try
                {
                    var afterId = _latestTelemetryEventId;
                    var isInitialLoad = !_logsInitialised || !afterId.HasValue;
                    var eventsResult = await DiagnosticsService.GetTelemetryEventsAsync(
                        afterId: isInitialLoad ? null : afterId,
                        beforeId: null,
                        cancellationToken: cancellationToken).ConfigureAwait(false);

                    if (eventsResult.IsSuccessful)
                    {
                        var mode = isInitialLoad ? TelemetryEventLoadMode.Initial : TelemetryEventLoadMode.Newer;
                        ApplyTelemetryEventsPage(eventsResult.Value, mode);

                        if (eventsResult.Value.LatestEventId.HasValue)
                        {
                            _latestTelemetryEventId = eventsResult.Value.LatestEventId;
                        }

                        if (eventsResult.Value.OldestEventId.HasValue)
                        {
                            if (!_oldestTelemetryEventId.HasValue || eventsResult.Value.OldestEventId.Value < _oldestTelemetryEventId.Value)
                            {
                                _oldestTelemetryEventId = eventsResult.Value.OldestEventId;
                            }
                        }

                        if (mode == TelemetryEventLoadMode.Initial)
                        {
                            _logsHasOlder = eventsResult.Value.HasMoreBefore;
                        }
                        else if (mode == TelemetryEventLoadMode.Newer)
                        {
                            _logsHasOlder = _logsHasOlder || eventsResult.Value.HasMoreBefore;
                        }

                        _logsHasNewer = eventsResult.Value.HasMoreAfter;
                        _logsInitialised = true;
                    }
                    else
                    {
                        var error = eventsResult.Error ?? new InvalidOperationException("Unknown telemetry events diagnostics error");
                        Logger.LogWarning(error, "Failed to refresh telemetry events stream.");
                        errorMessages.Add("Unable to retrieve telemetry events.");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Unexpected error refreshing telemetry events stream.");
                    errorMessages.Add("Unexpected error while retrieving telemetry events.");
                }

                _lastLogsRefreshUtc = nowUtc;
            }

            if (ShouldRefreshStorageMetrics() && nowUtc - _lastStorageRefreshUtc >= StorageRefreshInterval)
            {
                try
                {
                    var storageResult = await DiagnosticsService.GetDataStoreMetricsAsync(cancellationToken).ConfigureAwait(false);
                    if (storageResult.IsSuccessful)
                    {
                        _dataStoreMetrics = storageResult.Value;
                    }
                    else
                    {
                        var error = storageResult.Error ?? new InvalidOperationException("Unknown storage diagnostics error");
                        Logger.LogWarning(error, "Failed to refresh data store metrics snapshot.");
                        errorMessages.Add("Unable to retrieve storage metrics.");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Unexpected error refreshing data store metrics snapshot.");
                    errorMessages.Add("Unexpected error while retrieving storage metrics.");
                }

                _lastStorageRefreshUtc = nowUtc;
            }

            _errorMessage = errorMessages.Count > 0 ? string.Join(" ", errorMessages) : null;
            _isLoading = false;
        }
        finally
        {
            _refreshLock.Release();
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            await InvokeAsync(StateHasChanged);
        }
    }

    private void ApplyHistory(IReadOnlyList<BackgroundStackerHistorySample> samples)
    {
        _queueFillHistory.Clear();
        _queueLatencyHistory.Clear();
        _stackDurationHistory.Clear();

        if (samples.Count == 0)
        {
            return;
        }

        foreach (var sample in samples)
        {
            UpdateHistory(_queueFillHistory, sample.QueueFillPercentage);
            UpdateHistory(_queueLatencyHistory, sample.QueueLatencyMilliseconds ?? 0d);
            UpdateHistory(_stackDurationHistory, sample.StackDurationMilliseconds ?? 0d);
        }
    }

    private void ApplyFrameExportHistory(IReadOnlyList<FrameExportHistorySample> attempts)
    {
        _frameExportHistory = attempts;

        _frameExportLatencyHistory.Clear();
        _frameExportQueueLatencyHistory.Clear();
        _frameExportProcessingHistory.Clear();
        _frameExportFullPipelineHistory.Clear();

        if (attempts.Count == 0)
        {
            _lastFrameExportSample = null;
            return;
        }

        foreach (var attempt in attempts)
        {
            UpdateHistory(_frameExportLatencyHistory, attempt.LatencyMilliseconds ?? 0d);
            UpdateHistory(_frameExportQueueLatencyHistory, attempt.QueueLatencyMilliseconds ?? 0d);
            UpdateHistory(_frameExportProcessingHistory, attempt.ProcessingMilliseconds ?? 0d);
            UpdateHistory(_frameExportFullPipelineHistory, attempt.FullPipelineMilliseconds ?? 0d);
        }

        _lastFrameExportSample = attempts[^1];
    }

    private void ApplyRemoteDispatchHistory(IReadOnlyList<RemoteDispatchHistorySample> samples)
    {
        _remoteDispatchLatencyHistory.Clear();

        if (samples.Count == 0)
        {
            _lastRemoteDispatchSample = null;
            return;
        }

        foreach (var sample in samples)
        {
            UpdateHistory(_remoteDispatchLatencyHistory, sample.LatencyMilliseconds ?? 0d);
        }

        _lastRemoteDispatchSample = samples[^1];
    }

    private void ApplyTelemetryEventsPage(TelemetryEventPage page, TelemetryEventLoadMode mode)
    {
        if (mode == TelemetryEventLoadMode.Initial)
        {
            _telemetryEvents.Clear();
            _telemetryEventIds.Clear();
        }

        if (page.Events.Count > 0)
        {
            switch (mode)
            {
                case TelemetryEventLoadMode.Initial:
                    foreach (var entry in page.Events)
                    {
                        if (_telemetryEventIds.Add(entry.Id))
                        {
                            _telemetryEvents.Add(entry);
                        }
                    }

                    break;
                case TelemetryEventLoadMode.Newer:
                    foreach (var entry in page.Events)
                    {
                        if (_telemetryEventIds.Add(entry.Id))
                        {
                            _telemetryEvents.Insert(0, entry);
                        }
                    }

                    break;
                case TelemetryEventLoadMode.Older:
                    foreach (var entry in page.Events)
                    {
                        if (_telemetryEventIds.Add(entry.Id))
                        {
                            _telemetryEvents.Add(entry);
                        }
                    }

                    break;
            }
        }

        TrimTelemetryEvents();
        UpdateTelemetryEventBounds();
    }

    private void TrimTelemetryEvents(int maxCount = 500)
    {
        if (_telemetryEvents.Count <= maxCount)
        {
            return;
        }

        for (var index = _telemetryEvents.Count - 1; index >= maxCount; index--)
        {
            var removed = _telemetryEvents[index];
            _telemetryEvents.RemoveAt(index);
            _telemetryEventIds.Remove(removed.Id);
        }
    }

    private void UpdateTelemetryEventBounds()
    {
        if (_telemetryEvents.Count == 0)
        {
            _latestTelemetryEventId = null;
            _oldestTelemetryEventId = null;
            return;
        }

        _latestTelemetryEventId = _telemetryEvents[0].Id;
        _oldestTelemetryEventId = _telemetryEvents[^1].Id;
    }

    private void ApplyFrameExportFallbackFromMetrics(FrameExportMetricsSnapshot snapshot)
    {
        if (snapshot.Sinks.Count == 0)
        {
            return;
        }

        FrameExportHistorySample? latestSample = _lastFrameExportSample;

        foreach (var sink in snapshot.Sinks)
        {
            if (sink.LastAttemptLatencyMilliseconds is { } latency)
            {
                UpdateHistory(_frameExportLatencyHistory, latency);
            }

            if (sink.LastAttemptQueueLatencyMilliseconds is { } queueLatency)
            {
                UpdateHistory(_frameExportQueueLatencyHistory, queueLatency);
            }

            if (sink.LastAttemptProcessingMilliseconds is { } processing)
            {
                UpdateHistory(_frameExportProcessingHistory, processing);
            }

            if (sink.LastAttemptFullPipelineMilliseconds is { } fullPipeline)
            {
                UpdateHistory(_frameExportFullPipelineHistory, fullPipeline);
            }

            if (sink.LastAttemptAtUtc is { } attemptUtc)
            {
                var attemptLocal = sink.LastAttemptAtLocal ?? attemptUtc;
                var candidate = new FrameExportHistorySample(
                    FrameId: Guid.Empty,
                    AttemptedAtUtc: attemptUtc,
                    AttemptedAtLocal: attemptLocal,
                    Stage: sink.Stage,
                    SinkName: sink.SinkName,
                    Success: sink.LastAttemptSucceeded ?? false,
                    LatencyMilliseconds: sink.LastAttemptLatencyMilliseconds,
                    PayloadBytes: sink.LastAttemptPayloadBytes,
                    PayloadContentType: sink.LastAttemptContentType,
                    PayloadExtension: sink.LastAttemptExtension,
                    QueueLatencyMilliseconds: sink.LastAttemptQueueLatencyMilliseconds,
                    ProcessingMilliseconds: sink.LastAttemptProcessingMilliseconds,
                    FullPipelineMilliseconds: sink.LastAttemptFullPipelineMilliseconds,
                    FramesStacked: null,
                    IntegrationMilliseconds: null,
                    ErrorMessage: sink.LastAttemptSucceeded is true ? null : sink.LastFailureMessage);

                if (latestSample is null || candidate.AttemptedAtUtc > latestSample.AttemptedAtUtc)
                {
                    latestSample = candidate;
                }
            }
        }

        if (latestSample is not null)
        {
            _lastFrameExportSample = latestSample;
        }
    }

    private static void UpdateHistory(List<double> history, double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            value = 0d;
        }

        history.Add(value);
        if (history.Count > HistoryCapacity)
        {
            history.RemoveAt(0);
        }
    }

    private static string BuildDiagnosticsDownloadPath(string resource, string? query = null)
    {
        var path = $"{DiagnosticsApiBasePath}/{resource}";
        return string.IsNullOrWhiteSpace(query) ? path : $"{path}?{query}";
    }

    private static string BuildGaugeStyle(double percentage)
    {
        var clamped = Math.Clamp(double.IsNaN(percentage) ? 0d : percentage, 0d, 100d);
        var color = GetGaugeColor(clamped);
        return $"--value:{clamped:F1};--gauge-color:{color};";
    }

    private static string GetCpuCoreBarStyle(double usagePercent)
    {
        var clamped = Math.Clamp(double.IsNaN(usagePercent) ? 0d : usagePercent, 0d, 100d);
        var color = GetGaugeColor(clamped);
        return $"width:{clamped:F1}%;background:{color};";
    }

    private static string GetGaugeColor(double percentage) => percentage switch
    {
        >= 90 => "#dc3545",
        >= 75 => "#fd7e14",
        >= 55 => "#ffc107",
        >= 30 => "#0dcaf0",
        _ => "#198754"
    };

    private static double? CalculateMemoryUsagePercent(MemoryUsageSnapshot snapshot)
    {
        if (snapshot.TotalMegabytes.HasValue && snapshot.TotalMegabytes.Value > 0 && snapshot.UsedMegabytes.HasValue)
        {
            var percent = snapshot.UsedMegabytes.Value / snapshot.TotalMegabytes.Value * 100d;
            return Math.Clamp(percent, 0d, 100d);
        }

        return null;
    }

    private static string FormatPercent(double value) => $"{value:F1}%";

    private static string FormatPercent(double? value) => value.HasValue ? FormatPercent(value.Value) : "—";

    private static string FormatDepth(int depth, int capacity) => capacity > 0
        ? $"{depth.ToString("N0", CultureInfo.CurrentCulture)} / {capacity.ToString("N0", CultureInfo.CurrentCulture)}"
        : depth.ToString("N0", CultureInfo.CurrentCulture);

    private static string FormatCount(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    private static string FormatMemory(double megabytes)
    {
        if (double.IsNaN(megabytes) || double.IsInfinity(megabytes))
        {
            return "—";
        }

        return string.Create(CultureInfo.CurrentCulture, $"{megabytes:F2} MB");
    }

    private static string FormatMilliseconds(double? value) => value.HasValue
        ? string.Create(CultureInfo.CurrentCulture, $"{value.Value:F1} ms")
        : "—";

    private static string FormatSeconds(double? value)
    {
        if (!value.HasValue)
        {
            return "—";
        }

        var seconds = Math.Max(0d, value.Value);

        if (seconds < 1)
        {
            return string.Create(CultureInfo.CurrentCulture, $"{seconds:F3} s");
        }

        if (seconds < 10)
        {
            return string.Create(CultureInfo.CurrentCulture, $"{seconds:F2} s");
        }

        return string.Create(CultureInfo.CurrentCulture, $"{seconds:F1} s");
    }

    private static string FormatMegabytes(double? value)
    {
        if (!value.HasValue)
        {
            return "—";
        }

        return FormatMemory(value.Value);
    }

    private static string FormatBytes(long? bytes)
    {
        if (!bytes.HasValue || bytes.Value <= 0)
        {
            return "—";
        }

        var value = (double)bytes.Value;
        const double kilo = 1024d;
        const double mega = kilo * 1024d;
        const double giga = mega * 1024d;

        if (value >= giga)
        {
            return string.Create(CultureInfo.CurrentCulture, $"{value / giga:F2} GB");
        }

        if (value >= mega)
        {
            return string.Create(CultureInfo.CurrentCulture, $"{value / mega:F2} MB");
        }

        if (value >= kilo)
        {
            return string.Create(CultureInfo.CurrentCulture, $"{value / kilo:F1} KB");
        }

        return string.Create(CultureInfo.CurrentCulture, $"{value:F0} B");
    }

    private static string FormatDuration(double? seconds)
    {
        if (!seconds.HasValue)
        {
            return "—";
        }

        var duration = TimeSpan.FromSeconds(Math.Max(0d, seconds.Value));

        if (duration.TotalHours >= 1d)
        {
            return string.Create(CultureInfo.CurrentCulture, $"{(int)duration.TotalHours}h {duration.Minutes:D2}m");
        }

        if (duration.TotalMinutes >= 1d)
        {
            return string.Create(CultureInfo.CurrentCulture, $"{duration.Minutes:D2}m {duration.Seconds:D2}s");
        }

        return string.Create(CultureInfo.CurrentCulture, $"{duration.Seconds:D2}s");
    }

    private string BuildFrameExportPayloadDescriptor(FrameExportHistorySample? sample)
    {
        if (sample is null)
        {
            return "—";
        }

        var parts = new List<string>();

        var size = FormatBytes(sample.PayloadBytes);
        if (size != "—")
        {
            parts.Add(size);
        }

        var extension = NormalizeExtension(sample.PayloadExtension);
        if (!string.IsNullOrWhiteSpace(extension))
        {
            parts.Add(extension);
        }

        if (!string.IsNullOrWhiteSpace(sample.PayloadContentType))
        {
            parts.Add(sample.PayloadContentType);
        }

        if (sample.FramesStacked is { } stacked)
        {
            parts.Add(string.Create(CultureInfo.CurrentCulture, $"{stacked} frames"));
        }

        if (sample.IntegrationMilliseconds is { } integration)
        {
            parts.Add(string.Create(CultureInfo.CurrentCulture, $"{integration} ms integration"));
        }

        return parts.Count > 0 ? string.Join(" • ", parts) : "—";
    }

    private string BuildRemoteDispatchPayloadDescriptor()
    {
        if (_remoteDispatchMetrics is not { } metrics)
        {
            return "—";
        }

        var parts = new List<string>();

        var size = FormatBytes(metrics.LastPayloadBytes);
        if (size != "—")
        {
            parts.Add(size);
        }

        var extension = NormalizeExtension(metrics.LastPayloadExtension);
        if (!string.IsNullOrWhiteSpace(extension))
        {
            parts.Add(extension);
        }

        if (!string.IsNullOrWhiteSpace(metrics.LastPayloadContentType))
        {
            parts.Add(metrics.LastPayloadContentType);
        }

        return parts.Count > 0 ? string.Join(" • ", parts) : "—";
    }

    private static string? NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        var trimmed = extension.Trim();
    if (!trimmed.StartsWith(".", StringComparison.Ordinal))
        {
            trimmed = $".{trimmed}";
        }

        return trimmed;
    }

    private static string BuildHistoryDurationLabel(int sampleCount, TimeSpan cadence)
    {
        var cadenceSeconds = Math.Max(cadence.TotalSeconds, 0.1d);

        if (sampleCount <= 1)
        {
            var baselineSeconds = Math.Max(cadenceSeconds * 5, cadenceSeconds);
            return string.Create(CultureInfo.CurrentCulture, $"Rolling window < {baselineSeconds:F0} s");
        }

        var totalSeconds = sampleCount * cadenceSeconds;
        if (totalSeconds >= 90)
        {
            var minutes = totalSeconds / 60d;
            return string.Create(CultureInfo.CurrentCulture, $"Rolling window {minutes:F1} min");
        }

        return string.Create(CultureInfo.CurrentCulture, $"Rolling window {totalSeconds:F0} s");
    }

    private static string BuildMaxLabel(IReadOnlyCollection<double> values, string unit)
    {
        if (values.Count == 0)
        {
            return "No samples yet";
        }

        var max = values.Max();
        return string.Create(CultureInfo.CurrentCulture, $"Max {max:F1} {unit}");
    }

    private static string DescribeQueuePressure(int level) => level switch
    {
        <= 0 => "Nominal",
        1 => "Rising",
        2 => "Elevated",
        _ => "High"
    };

    private static string FormatFrameExportStage(FrameExportStage stage) => stage switch
    {
        FrameExportStage.Raw => "Raw",
        FrameExportStage.Processed => "Processed",
        _ => stage.ToString()
    };

    private static string FormatFrameExportAttemptOutcome(bool success) => success ? "Succeeded" : "Failed";

    private static string FormatRemoteDispatchOutcome(RemoteDispatchOutcome outcome) => outcome switch
    {
        RemoteDispatchOutcome.Disabled => "Disabled",
        RemoteDispatchOutcome.Succeeded => "Succeeded",
        RemoteDispatchOutcome.Skipped => "Skipped",
        RemoteDispatchOutcome.Failed => "Failed",
        _ => outcome.ToString()
    };

    private static string GetLogSeverityBadgeClass(string severity)
    {
        if (string.IsNullOrWhiteSpace(severity))
        {
            return "badge badge-log badge-log--info";
        }

        return severity.Trim().ToLowerInvariant() switch
        {
            "critical" => "badge badge-log badge-log--critical",
            "fatal" => "badge badge-log badge-log--critical",
            "error" => "badge badge-log badge-log--error",
            "warning" => "badge badge-log badge-log--warning",
            "warn" => "badge badge-log badge-log--warning",
            "debug" => "badge badge-log badge-log--debug",
            "trace" => "badge badge-log badge-log--trace",
            _ => "badge badge-log badge-log--info"
        };
    }

    private static string DescribeVacuumResult(TelemetryRetentionSummaryMetrics retention) => retention switch
    {
        { VacuumAttempted: false } => "Not attempted",
        { VacuumSucceeded: true } => "Succeeded",
        _ => "Failed"
    };

    private static string DescribeBootstrap(DataStoreBootstrapStatusMetrics bootstrap) => bootstrap switch
    {
        { Ran: false } => "Not run yet",
        { Succeeded: true } => "Completed successfully",
        _ => "Failed"
    };

    private static string FormatTimestamp(DateTimeOffset? timestamp)
        => timestamp.HasValue
            ? timestamp.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture)
            : "—";

    private string GetFilterBarStyle(FilterMetrics metric)
    {
        var max = _filterMetrics is { Filters.Count: > 0 }
            ? _filterMetrics.Filters.Max(x => x.AverageDurationMilliseconds ?? x.LastDurationMilliseconds ?? 0d)
            : 0d;

        if (max <= 0)
        {
            max = 1d;
        }

        var baseline = metric.AverageDurationMilliseconds ?? metric.LastDurationMilliseconds ?? 0d;
        var percent = Math.Clamp((baseline / max) * 100d, 5d, 100d);
        var color = baseline switch
        {
            >= 20 => "#dc3545",
            >= 10 => "#fd7e14",
            >= 5 => "#ffc107",
            >= 2 => "#0dcaf0",
            _ => "#0d6efd"
        };

        return $"width:{percent:F1}%;background:{color};";
    }

    private enum DiagnosticsTab
    {
        Overview,
        Pipeline,
        Dispatch,
        Exports,
        Logs,
        Storage
    }

    private enum TelemetryEventLoadMode
    {
        Initial,
        Newer,
        Older
    }

    private bool ShouldRefreshFrameExportMetrics() => ActiveTab is DiagnosticsTab.Exports;

    private bool ShouldRefreshRemoteDispatchMetrics() => ActiveTab is DiagnosticsTab.Dispatch;

    private bool ShouldRefreshQueueMetrics() => ActiveTab is DiagnosticsTab.Pipeline;

    private bool ShouldRefreshFilterMetrics() => ActiveTab is DiagnosticsTab.Pipeline;

    private bool ShouldRefreshSystemMetrics() => ActiveTab is DiagnosticsTab.Overview;

    private bool ShouldRefreshLogs() => ActiveTab is DiagnosticsTab.Logs;

    private bool ShouldRefreshStorageMetrics() => ActiveTab is DiagnosticsTab.Storage;
}

    internal enum RemoteDispatchConfigurationStatus
    {
        Disabled,
        Enabled,
        Warning
    }

    internal sealed record RemoteDispatchConfigSnapshot(
        RemoteDispatchConfigurationStatus Status,
        RemoteDispatchMode Mode,
        string Summary,
        string? Bucket,
        string? Endpoint,
        RemoteDispatchImageFormat ImageFormat,
        bool UseSsl,
        IReadOnlyList<string> Issues)
    {
        public static RemoteDispatchConfigSnapshot Disabled { get; } = new(
            RemoteDispatchConfigurationStatus.Disabled,
            RemoteDispatchMode.None,
            "Remote dispatch is disabled in configuration.",
            null,
            null,
            RemoteDispatchImageFormat.Png,
            false,
            Array.Empty<string>());
    }
