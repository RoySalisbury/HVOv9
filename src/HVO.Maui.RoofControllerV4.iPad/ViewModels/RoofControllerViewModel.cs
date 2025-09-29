using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HVO.Maui.RoofControllerV4.iPad.Configuration;
using HVO.Maui.RoofControllerV4.iPad.Models;
using HVO.Maui.RoofControllerV4.iPad.Services;
using HVO.WebSite.RoofControllerV4.Models;
using HVO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Graphics;

namespace HVO.Maui.RoofControllerV4.iPad.ViewModels;

/// <summary>
/// Presentation model for the iPad roof controller experience.
/// </summary>
public sealed partial class RoofControllerViewModel : ObservableObject, IDisposable
{
    private readonly IRoofControllerApiClient _apiClient;
    private readonly RoofControllerApiOptions _options;
    private readonly ILogger<RoofControllerViewModel> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly ObservableCollection<NotificationItem> _notifications = new();
    private readonly ReadOnlyObservableCollection<NotificationItem> _readonlyNotifications;

    private CancellationTokenSource? _pollingCts;
    private Task? _pollingTask;
    private bool _hasInitialized;
    private bool _serviceUnavailableNotified;
    private RoofControllerStatus _previousStatus = RoofControllerStatus.Unknown;
    private RoofControllerStopReason _previousStopReason = RoofControllerStopReason.None;

    public RoofControllerViewModel(
        IRoofControllerApiClient apiClient,
        IOptions<RoofControllerApiOptions> options,
        ILogger<RoofControllerViewModel> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _readonlyNotifications = new ReadOnlyObservableCollection<NotificationItem>(_notifications);
    }

    #region Bindable State

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearFaultCommand))]
    private bool isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearFaultCommand))]
    private bool isServiceAvailable;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearFaultCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    private bool isMoving;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    private RoofControllerStatus currentStatus = RoofControllerStatus.Unknown;

    [ObservableProperty]
    private RoofControllerStopReason lastStopReason = RoofControllerStopReason.None;

    [ObservableProperty]
    private DateTimeOffset? lastTransitionUtc;

    [ObservableProperty]
    private bool isWatchdogActive;

    [ObservableProperty]
    private double watchdogSecondsRemaining;

    [ObservableProperty]
    private string? serviceBannerTitle;

    [ObservableProperty]
    private string? serviceBannerMessage;

    [ObservableProperty]
    private bool showServiceBanner;

    [ObservableProperty]
    private bool serviceBannerIsError;

    [ObservableProperty]
    private string? lastErrorMessage;

    #endregion

    public IReadOnlyList<NotificationItem> Notifications => _readonlyNotifications;

    public bool HasFault => CurrentStatus == RoofControllerStatus.Error;

    public bool WasEmergencyStop => LastStopReason is RoofControllerStopReason.EmergencyStop or RoofControllerStopReason.SafetyWatchdogTimeout;

    public string LastStopReasonLabel => LastStopReason switch
    {
        RoofControllerStopReason.EmergencyStop or RoofControllerStopReason.SafetyWatchdogTimeout => "Emergency",
        RoofControllerStopReason.None => string.Empty,
        _ => "Normal"
    };

    public Color InitializationBadgeColor => IsInitialized ? Color.FromArgb("#198754") : Color.FromArgb("#6c757d");

    public string InitializationBadgeText => IsInitialized ? "Initialized" : "Initializing…";

    public string StatusText => CurrentStatus switch
    {
        RoofControllerStatus.Unknown => "Unknown",
        RoofControllerStatus.NotInitialized => "Initializing…",
        RoofControllerStatus.Open => "Open",
        RoofControllerStatus.Opening => "Opening",
        RoofControllerStatus.Closed => "Closed",
        RoofControllerStatus.Closing => "Closing",
        RoofControllerStatus.Stopped => "Stopped",
        RoofControllerStatus.PartiallyOpen => "Partially Open",
        RoofControllerStatus.PartiallyClose => "Partially Closed",
        RoofControllerStatus.Error => "Fault",
        _ => CurrentStatus.ToString()
    };

    public string HealthStatus => HasFault ? "Error Detected" : IsServiceAvailable ? "Healthy" : "Offline";

    public string MovementStatus => IsMoving ? "In Progress" : "Idle";

    public bool IsInitialized => CurrentStatus != RoofControllerStatus.Unknown && CurrentStatus != RoofControllerStatus.NotInitialized;

    public string LastTransitionDisplay => LastTransitionUtc is null
        ? "—"
        : LastTransitionUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public double WatchdogPercentage
    {
        get
        {
            if (!IsWatchdogActive)
            {
                return 0d;
            }

            var timeout = _options.SafetyWatchdogTimeoutSeconds;
            if (timeout is null or <= 0)
            {
                return 0d;
            }

            if (WatchdogSecondsRemaining <= 0)
            {
                return 1d;
            }

            return Math.Clamp(1d - (WatchdogSecondsRemaining / timeout.Value), 0d, 1d);
        }
    }

    public Color StatusBadgeColor => CurrentStatus switch
    {
        RoofControllerStatus.Open => Color.FromArgb("#198754"),
        RoofControllerStatus.Closed => Color.FromArgb("#6c757d"),
        RoofControllerStatus.Opening or RoofControllerStatus.Closing => Color.FromArgb("#0dcaf0"),
        RoofControllerStatus.Error => Color.FromArgb("#dc3545"),
        _ => Color.FromArgb("#343a40")
    };

    public Color HealthBadgeColor => HasFault ? Color.FromArgb("#dc3545") : Color.FromArgb("#198754");

    public Color MovementBadgeColor => IsMoving ? Color.FromArgb("#0d6efd") : Color.FromArgb("#6c757d");

    public bool ShowStopBadge => !string.IsNullOrEmpty(LastStopReasonLabel);

    public Color StopBadgeColor => WasEmergencyStop ? Color.FromArgb("#dc3545") : Color.FromArgb("#6c757d");

    public Uri? CameraStreamUri => _options.GetCameraStreamUri();

    public string? CameraStreamUrl => CameraStreamUri?.ToString();

    public bool HasNotifications => _notifications.Count > 0;

    public string WatchdogStatusText => IsWatchdogActive ? "Active" : "Standby";

    public string WatchdogSecondaryText
    {
        get
        {
            if (IsWatchdogActive)
            {
                return $"{Math.Max(WatchdogSecondsRemaining, 0):F0}s remaining";
            }

            return _options.SafetyWatchdogTimeoutSeconds is double timeout
                ? $"Timeout {timeout:F0}s"
                : "Timeout not configured";
        }
    }

    public Color ServiceBannerColor => ServiceBannerIsError ? Color.FromArgb("#dc3545") : Color.FromArgb("#f0ad4e");

    public bool CanOpen() => !IsBusy && IsServiceAvailable && !IsMoving && CurrentStatus is not (RoofControllerStatus.Open or RoofControllerStatus.Opening or RoofControllerStatus.Error);

    public bool CanClose() => !IsBusy && IsServiceAvailable && !IsMoving && CurrentStatus is not (RoofControllerStatus.Closed or RoofControllerStatus.Closing or RoofControllerStatus.Error);

    public bool CanStop() => !IsBusy && IsServiceAvailable && IsMoving;

    public bool CanClearFault() => !IsBusy && IsServiceAvailable && !IsMoving;

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private async Task OpenAsync()
    {
        await ExecuteCommandAsync(() => _apiClient.OpenAsync(), "Command", "Opening roof", "Failed to open roof");
    }

    [RelayCommand(CanExecute = nameof(CanClose))]
    private async Task CloseAsync()
    {
        await ExecuteCommandAsync(() => _apiClient.CloseAsync(), "Command", "Closing roof", "Failed to close roof");
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        await ExecuteCommandAsync(() => _apiClient.StopAsync(), "Command", "Stop requested", "Failed to stop roof");
    }

    [RelayCommand(CanExecute = nameof(CanClearFault))]
    private async Task ClearFaultAsync()
    {
        await ExecuteCommandAsync(async () =>
        {
            var result = await _apiClient.ClearFaultAsync(_options.ClearFaultPulseMs).ConfigureAwait(false);
            if (result.IsSuccessful && result.Value)
            {
                return await _apiClient.GetStatusAsync().ConfigureAwait(false);
            }

            return Result<RoofStatusResponse>.Failure(result.Error ?? new InvalidOperationException("Clear fault failed"));
        }, "Command", "Fault cleared", "Failed to clear fault");
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_hasInitialized)
        {
            return;
        }

        _hasInitialized = true;
        await RefreshStatusAsync(true, cancellationToken).ConfigureAwait(false);
        StartPolling();
    }

    public async Task RefreshStatusAsync(bool initialLoad = false, CancellationToken cancellationToken = default)
    {
        if (!await _refreshLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            if (initialLoad)
            {
                await MainThread.InvokeOnMainThreadAsync(() => IsBusy = true);
            }

            var result = await _apiClient.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            if (result.IsSuccessful)
            {
                await HandleStatusAsync(result.Value).ConfigureAwait(false);
                UpdateServiceBanner(isError: false, title: null, message: null);
                _serviceUnavailableNotified = false;
            }
            else
            {
                await HandleServiceFailureAsync(result.Error!).ConfigureAwait(false);
            }
        }
        finally
        {
            if (initialLoad)
            {
                await MainThread.InvokeOnMainThreadAsync(() => IsBusy = false);
            }

            _refreshLock.Release();
        }
    }

    private async Task ExecuteCommandAsync(Func<Task<Result<RoofStatusResponse>>> command, string notificationTitle, string successMessage, string failureMessage)
    {
        if (!await _refreshLock.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            await MainThread.InvokeOnMainThreadAsync(() => IsBusy = true);
            var result = await command().ConfigureAwait(false);
            if (result.IsSuccessful)
            {
                await HandleStatusAsync(result.Value).ConfigureAwait(false);
                AddNotification(notificationTitle, successMessage, NotificationLevel.Info);
            }
            else
            {
                await HandleServiceFailureAsync(result.Error!).ConfigureAwait(false);
                AddNotification("Error", failureMessage, NotificationLevel.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing roof controller command");
            await HandleServiceFailureAsync(ex).ConfigureAwait(false);
            AddNotification("Error", failureMessage, NotificationLevel.Error);
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() => IsBusy = false);
            _refreshLock.Release();
        }
    }

    private async Task HandleStatusAsync(RoofStatusResponse status)
    {
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            IsServiceAvailable = true;
            LastErrorMessage = null;
            CurrentStatus = status.Status;
            IsMoving = status.IsMoving;
            LastStopReason = status.LastStopReason;
            LastTransitionUtc = status.LastTransitionUtc;
            IsWatchdogActive = status.IsWatchdogActive;
            WatchdogSecondsRemaining = status.WatchdogSecondsRemaining ?? 0d;

            if (_previousStatus != status.Status)
            {
                AddNotification("Status", $"Roof is now {StatusText}", NotificationLevel.Info);
            }

            if (WasEmergencyStop && _previousStopReason != LastStopReason)
            {
                AddNotification("Safety", "Emergency stop triggered", NotificationLevel.Error);
            }

            _previousStatus = status.Status;
            _previousStopReason = status.LastStopReason;

            OpenCommand.NotifyCanExecuteChanged();
            CloseCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
            ClearFaultCommand.NotifyCanExecuteChanged();

            OnPropertyChanged(nameof(HasFault));
            OnPropertyChanged(nameof(WasEmergencyStop));
            OnPropertyChanged(nameof(LastStopReasonLabel));
            OnPropertyChanged(nameof(ShowStopBadge));
            OnPropertyChanged(nameof(StopBadgeColor));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(HealthStatus));
            OnPropertyChanged(nameof(MovementStatus));
            OnPropertyChanged(nameof(IsInitialized));
            OnPropertyChanged(nameof(InitializationBadgeColor));
            OnPropertyChanged(nameof(InitializationBadgeText));
            OnPropertyChanged(nameof(LastTransitionDisplay));
            OnPropertyChanged(nameof(StatusBadgeColor));
            OnPropertyChanged(nameof(HealthBadgeColor));
            OnPropertyChanged(nameof(MovementBadgeColor));
            OnPropertyChanged(nameof(WatchdogPercentage));
            OnPropertyChanged(nameof(WatchdogStatusText));
            OnPropertyChanged(nameof(WatchdogSecondaryText));
        });
    }

    private async Task HandleServiceFailureAsync(Exception error)
    {
        _logger.LogError(error, "Roof controller API error");
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            IsServiceAvailable = false;
            LastErrorMessage = error.Message;
            ShowServiceBanner = true;
            ServiceBannerTitle = "Service unavailable";
            ServiceBannerMessage = "The roof controller API could not be reached. Check connectivity and try again.";
            ServiceBannerIsError = true;

            if (!_serviceUnavailableNotified)
            {
                AddNotification("Service", "Roof controller offline", NotificationLevel.Warning);
                _serviceUnavailableNotified = true;
            }

            OpenCommand.NotifyCanExecuteChanged();
            CloseCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
            ClearFaultCommand.NotifyCanExecuteChanged();

            OnPropertyChanged(nameof(HealthStatus));
            OnPropertyChanged(nameof(MovementStatus));
            OnPropertyChanged(nameof(IsInitialized));
            OnPropertyChanged(nameof(InitializationBadgeColor));
            OnPropertyChanged(nameof(InitializationBadgeText));
            OnPropertyChanged(nameof(ShowStopBadge));
            OnPropertyChanged(nameof(StopBadgeColor));
            OnPropertyChanged(nameof(StatusBadgeColor));
            OnPropertyChanged(nameof(HealthBadgeColor));
            OnPropertyChanged(nameof(MovementBadgeColor));
            OnPropertyChanged(nameof(WatchdogStatusText));
            OnPropertyChanged(nameof(WatchdogSecondaryText));
        });
    }

    private void UpdateServiceBanner(bool isError, string? title, string? message)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(message))
            {
                ShowServiceBanner = false;
                ServiceBannerTitle = null;
                ServiceBannerMessage = null;
                ServiceBannerIsError = false;
                return;
            }

            ShowServiceBanner = true;
            ServiceBannerTitle = title;
            ServiceBannerMessage = message;
            ServiceBannerIsError = isError;
        });
    }

    private void AddNotification(string title, string message, NotificationLevel level)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _notifications.Insert(0, new NotificationItem
            {
                Title = title,
                Message = message,
                Level = level,
                Timestamp = DateTime.Now
            });

            while (_notifications.Count > 5)
            {
                _notifications.RemoveAt(_notifications.Count - 1);
            }

            OnPropertyChanged(nameof(HasNotifications));
        });
    }

    private void StartPolling()
    {
        StopPolling();
        var interval = TimeSpan.FromSeconds(Math.Max(_options.StatusPollIntervalSeconds, 1));
        _pollingCts = new CancellationTokenSource();
        var token = _pollingCts.Token;

        _pollingTask = Task.Run(async () =>
        {
            await Task.Delay(interval, token).ConfigureAwait(false);
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await RefreshStatusAsync(cancellationToken: token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while polling roof status");
                }

                try
                {
                    await Task.Delay(interval, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, token);
    }

    private void StopPolling()
    {
        if (_pollingCts is null)
        {
            return;
        }

        try
        {
            _pollingCts.Cancel();
            _pollingTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error stopping polling task");
        }
        finally
        {
            _pollingCts.Dispose();
            _pollingCts = null;
            _pollingTask = null;
        }
    }

    public void Dispose()
    {
        StopPolling();
        _refreshLock.Dispose();
        _pollingCts?.Dispose();
    }

    partial void OnServiceBannerIsErrorChanged(bool value)
    {
        MainThread.BeginInvokeOnMainThread(() => OnPropertyChanged(nameof(ServiceBannerColor)));
    }
}
