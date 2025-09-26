
using System;
using HVO.WebSite.RoofControllerV4.Models;
using HVO.Iot.Devices.Iot.Devices.Sequent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.WebSite.RoofControllerV4.Logic
{
public class RoofControllerServiceV4 : IRoofControllerServiceV4, IAsyncDisposable, IDisposable
{
    protected readonly object _syncLock = new object();
    protected volatile bool _disposed;  // Make volatile for thread-safe checking

    // Safety watchdog fields - use single lock for atomicity
    protected System.Timers.Timer? _safetyWatchdogTimer;
    protected System.Timers.Timer? _periodicVerificationTimer;
    protected DateTime _operationStartTime;
    private bool _watchdogActive;

    protected RoofControllerCommandIntent _lastCommandIntent = RoofControllerCommandIntent.None;
    protected RoofControllerStopReason _lastStopReason = RoofControllerStopReason.None;

    private readonly ILogger<RoofControllerServiceV4> _logger;
    private readonly RoofControllerOptionsV4 _roofControllerOptions;
    private readonly FourRelayFourInputHat _fourRelayFourInputHat;
    // Cached LED mask to avoid redundant I2C writes (bits 0..2 -> OpenLimit, ClosedLimit, Fault)
    private byte? _lastIndicatorLedMask;

    // Forward digital input events from the HAT
    private EventHandler<bool>? _hatIn1Handler;
    private EventHandler<bool>? _hatIn2Handler;
    private EventHandler<bool>? _hatIn3Handler;
    private EventHandler<bool>? _hatIn4Handler;
    private bool InputsEventsActive => _hatIn1Handler is not null || _hatIn2Handler is not null || _hatIn3Handler is not null || _hatIn4Handler is not null;

    // Last-known input states (null until first known)
    private bool? _lastIn1;
    private bool? _lastIn2;
    private bool? _lastIn3;
    private bool? _lastIn4;

    // Legacy DigitalInput1..4 events removed; use named alias events below

    // Event hooks are handled internally and exposed via protected virtual methods instead of public events
    protected virtual void OnForwardLimitSwitchChanged(bool isHigh)
    {
        // Interpret raw state based on configured polarity. For NC: HIGH = normal (not at limit), LOW = limit reached.
        lock (_syncLock)
        {
            _logger.LogDebug("ForwardLimitSwitchChanged RawHigh={State}", isHigh);
            _lastIn1 = isHigh; // store raw electrical level (HIGH = circuit closed / not at limit)
            bool limitReached = _roofControllerOptions.UseNormallyClosedLimitSwitches ? !isHigh : isHigh;
            if (limitReached)
            {
                InternalStop(RoofControllerStopReason.LimitSwitchReached);
                _lastCommandIntent = RoofControllerCommandIntent.LimitStop;
            }
            else
            {
                UpdateRoofStatus();
            }
        }
    }
    protected virtual void OnReverseLimitSwitchChanged(bool isHigh)
    {
        // Interpret raw state based on configured polarity (see forward handler).
        lock (_syncLock)
        {
            _logger.LogDebug("ReverseLimitSwitchChanged RawHigh={State}", isHigh);
            _lastIn2 = isHigh;
            bool limitReached = _roofControllerOptions.UseNormallyClosedLimitSwitches ? !isHigh : isHigh;
            if (limitReached)
            {
                InternalStop(RoofControllerStopReason.LimitSwitchReached);
                _lastCommandIntent = RoofControllerCommandIntent.LimitStop;
            }
            else
            {
                UpdateRoofStatus();
            }
        }
    }
    protected virtual void OnFaultNotificationChanged(bool isHigh)
    {
        _logger.LogDebug("FaultNotificationChanged: {State}", isHigh);
        lock (_syncLock)
        {
            _lastIn3 = isHigh;
            if (isHigh)
            {
                // Fail-safe: stop movement immediately on fault and set error
                InternalStop(RoofControllerStopReason.EmergencyStop);
                if (Status != RoofControllerStatus.Error)
                {
                    Status = RoofControllerStatus.Error;
                    LastTransitionUtc = DateTimeOffset.UtcNow;
                }
                _lastCommandIntent = RoofControllerCommandIntent.FaultStop;
            }
            else
            {
                UpdateRoofStatus();
            }
        }
    }
    protected virtual void OnRoofMovementNotificationChanged(bool isHigh)
    {
        _logger.LogDebug("RoofMovementNotificationChanged: {State}", isHigh);
        // Movement notification can help infer motion between limits
        lock (_syncLock)
        {
            _lastIn4 = isHigh;
            UpdateRoofStatus();
        }
    }


    public event EventHandler<RoofStatusChangedEventArgs>? StatusChanged;


    public RoofControllerServiceV4(ILogger<RoofControllerServiceV4> logger, IOptions<RoofControllerOptionsV4> roofControllerOptions, FourRelayFourInputHat fourRelayFourInputHat)
    {
        this._logger = logger;
        this._roofControllerOptions = roofControllerOptions.Value;
        this._fourRelayFourInputHat = fourRelayFourInputHat;
    }


    /// <summary>
    /// Returns true if the safety watchdog is currently active (roof in motion and timer running).
    /// </summary>
    public bool IsWatchdogActive
    {
        get { lock (_syncLock) { return _watchdogActive; } }
    }

    /// <summary>
    /// Returns seconds remaining on the watchdog timer, or null if not active.
    /// </summary>
    public double? WatchdogSecondsRemaining
    {
        get
        {
            lock (_syncLock)
            {
                if (!_watchdogActive) return null;
                var elapsed = (DateTime.UtcNow - _operationStartTime).TotalSeconds;
                var remain = _roofControllerOptions.SafetyWatchdogTimeout.TotalSeconds - elapsed;
                return remain > 0 ? remain : 0;
            }
        }
    }

    public virtual bool IsInitialized { get; protected set; } = false;

    public virtual RoofControllerStatus Status { get; protected set; } = RoofControllerStatus.NotInitialized;
    public virtual DateTimeOffset? LastTransitionUtc { get; protected set; }
    public bool IsServiceDisposed => _disposed;

    public bool AtSpeedRun
    {
        get
        {
            lock (_syncLock)
            {
                if (!(InputsEventsActive && _lastIn4.HasValue))
                {
                    // lightweight refresh
                    ForceReadInputs_NoLock();
                }
                return _lastIn4 ?? false;
            }
        }
    }

    public RoofStatusResponse GetCurrentStatusSnapshot()
    {
        lock (_syncLock)
        {
            return new RoofStatusResponse(
                Status,
                IsMoving,
                LastStopReason,
                LastTransitionUtc,
                IsWatchdogActive,
                WatchdogSecondsRemaining,
                _lastIn4 ?? false
            );
        }
    }

    /// <summary>
    /// Gets a value indicating whether the roof is currently moving (opening or closing).
    /// This property returns true when the roof is actively in motion and not at a limit switch position.
    /// </summary>
    public virtual bool IsMoving => Status == RoofControllerStatus.Opening || Status == RoofControllerStatus.Closing;

    /// <summary>
    /// Gets the reason for the last stop operation.
    /// </summary>
    public virtual RoofControllerStopReason LastStopReason
    {
        get
        {
            lock (_syncLock)
            {
                return _lastStopReason;
            }
        }
        protected set
        {
            lock (_syncLock)
            {
                _lastStopReason = value;
            }
        }
    }


    public virtual Task<Result<bool>> Initialize(CancellationToken cancellationToken)
    {
        if (this._disposed)
        {
            return Task.FromResult(Result<bool>.Failure(new ObjectDisposedException(nameof(RoofControllerServiceV4))));
        }

        lock (this._syncLock)
        {
            if (this.IsInitialized)
            {
                return Task.FromResult(Result<bool>.Failure(new InvalidOperationException("Already Initialized")));
            }

            // Setup the cancellation token registration so we know when things are shutting down as soon as possible and can call STOP.
            cancellationToken.Register(() => this.Stop());

            // Initialize the safety watchdog timer
            this.InitializeSafetyWatchdog();
            // Initialize periodic verification timer (created only if enabled)
            this.InitializeOrUpdatePeriodicVerificationTimer();

            // Optionally set HAT poll interval
            try
            {
                _fourRelayFourInputHat.DigitalInputPollInterval = _roofControllerOptions.DigitalInputPollInterval;
            }
            catch { }

            // Subscribe to HAT input events and forward them if enabled
            if (_roofControllerOptions.EnableDigitalInputPolling)
            {
                _hatIn1Handler = (_, s) => OnForwardLimitSwitchChanged(s);
                _hatIn2Handler = (_, s) => OnReverseLimitSwitchChanged(s);
                _hatIn3Handler = (_, s) => OnFaultNotificationChanged(s);
                _hatIn4Handler = (_, s) => OnRoofMovementNotificationChanged(s);

                _fourRelayFourInputHat.DigitalInput1Changed += _hatIn1Handler;
                _fourRelayFourInputHat.DigitalInput2Changed += _hatIn2Handler;
                _fourRelayFourInputHat.DigitalInput3Changed += _hatIn3Handler;
                _fourRelayFourInputHat.DigitalInput4Changed += _hatIn4Handler;
            }

            // Always reset to a known safe state on initialization. Using the InternalStop will bypass the initialization check.
            this.InternalStop(RoofControllerStopReason.None);

            this.IsInitialized = true;
            return Task.FromResult(Result<bool>.Success(this.IsInitialized));
        }
    }

    /// <summary>
    /// Initializes the safety watchdog timer that prevents runaway operations.
    /// </summary>
    protected virtual void InitializeSafetyWatchdog()
    {
        lock (_syncLock)
        {
            RecreateSafetyWatchdog_NoLock();
        }
    }

    private void RecreateSafetyWatchdog_NoLock()
    {
        if (_safetyWatchdogTimer != null)
        {
            _safetyWatchdogTimer.Stop();
            _safetyWatchdogTimer.Elapsed -= SafetyWatchdog_Elapsed;
            _safetyWatchdogTimer.Dispose();
        }
        _safetyWatchdogTimer = new System.Timers.Timer(_roofControllerOptions.SafetyWatchdogTimeout.TotalMilliseconds)
        {
            AutoReset = false
        };
        _safetyWatchdogTimer.Elapsed += SafetyWatchdog_Elapsed;
        _watchdogActive = false; // reset active state on recreation
    }

    private void InitializeOrUpdatePeriodicVerificationTimer()
    {
        lock (_syncLock)
        {
            if (_periodicVerificationTimer != null)
            {
                _periodicVerificationTimer.Stop();
                _periodicVerificationTimer.Elapsed -= PeriodicVerification_Elapsed;
                _periodicVerificationTimer.Dispose();
                _periodicVerificationTimer = null;
            }
            if (_roofControllerOptions.EnablePeriodicVerificationWhileMoving)
            {
                _periodicVerificationTimer = new System.Timers.Timer(_roofControllerOptions.PeriodicVerificationInterval.TotalMilliseconds);
                _periodicVerificationTimer.AutoReset = true;
                _periodicVerificationTimer.Elapsed += PeriodicVerification_Elapsed;
            }
        }
    }

    /// <summary>
    /// Starts the safety watchdog timer for the current operation.
    /// </summary>
    protected virtual void StartSafetyWatchdog()
    {
        lock (_syncLock)
        {
            if (_safetyWatchdogTimer != null)
            {
                // Recreate timer instance for reliable restart
                RecreateSafetyWatchdog_NoLock();

                _operationStartTime = DateTime.UtcNow;
                _safetyWatchdogTimer.Start();
                _watchdogActive = true;
                _logger.LogInformation("Safety watchdog started for {timeout} seconds", _roofControllerOptions.SafetyWatchdogTimeout.TotalSeconds);
                TryStartPeriodicVerification_NoLock();
                RaiseStatusChanged_NoLock();
            }
        }
    }

    /// <summary>
    /// Stops the safety watchdog timer.
    /// </summary>
    protected virtual void StopSafetyWatchdog()
    {
        lock (_syncLock)
        {
            StopSafetyWatchdog_NoLock();
        }
    }

    private void StopSafetyWatchdog_NoLock()
    {
        if (_safetyWatchdogTimer != null && _watchdogActive)
        {
            _safetyWatchdogTimer.Stop();
            var elapsed = DateTime.UtcNow - _operationStartTime;
            if (elapsed.TotalSeconds >= 0 && elapsed.TotalSeconds < (_roofControllerOptions.SafetyWatchdogTimeout.TotalSeconds * 10))
            {
                // Only log if the elapsed time is sane (prevents huge numbers when never started)
                _logger.LogInformation("Safety watchdog stopped after {elapsed} seconds", elapsed.TotalSeconds);
            }
            _watchdogActive = false;
        }
        StopPeriodicVerification_NoLock();
        RaiseStatusChanged_NoLock();
    }

    private void TryStartPeriodicVerification_NoLock()
    {
        if (_roofControllerOptions.EnablePeriodicVerificationWhileMoving && _periodicVerificationTimer != null)
        {
            if (!_periodicVerificationTimer.Enabled)
            {
                _periodicVerificationTimer.Start();
                _logger.LogDebug("Periodic verification started IntervalSeconds={Interval}", _roofControllerOptions.PeriodicVerificationInterval.TotalSeconds);
            }
        }
    }

    private void StopPeriodicVerification_NoLock()
    {
        if (_periodicVerificationTimer != null && _periodicVerificationTimer.Enabled)
        {
            _periodicVerificationTimer.Stop();
            _logger.LogDebug("Periodic verification stopped");
        }
    }

    private void PeriodicVerification_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        try
        {
            lock (_syncLock)
            {
                if (_disposed) return;
                if (Status == RoofControllerStatus.Opening || Status == RoofControllerStatus.Closing)
                {
                    _logger.LogTrace("Periodic verification tick - forcing hardware status refresh");
                    UpdateRoofStatus(forceRead: true);
                }
                else
                {
                    // Not moving; pause until next motion
                    StopPeriodicVerification_NoLock();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Periodic verification tick failed");
        }
    }

    /// <summary>
    /// Safety watchdog timer elapsed event handler - emergency stops the roof.
    /// Thread-safe with proper disposal checking and atomic hardware operations.
    /// </summary>
    protected virtual void SafetyWatchdog_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        _logger.LogWarning("SAFETY WATCHDOG TRIGGERED: Roof operation exceeded maximum allowed time of {timeout} seconds. Emergency stopping roof.",
            _roofControllerOptions.SafetyWatchdogTimeout.TotalSeconds);

        try
        {
            // Emergency stop - bypass normal checks but check disposal
            lock (_syncLock)
            {
                // Check if we're disposed - if so, don't perform any GPIO operations
                if (IsDisposed)
                    return;

                InternalStop(RoofControllerStopReason.SafetyWatchdogTimeout);
                var previousStatus = Status;
                Status = RoofControllerStatus.Error;
                if (previousStatus != RoofControllerStatus.Error)
                {
                    LastTransitionUtc = DateTimeOffset.UtcNow;
                }
                _lastCommandIntent = RoofControllerCommandIntent.SafetyStop;
                _watchdogActive = false;


                _logger.LogError("Roof stopped by safety watchdog - manual intervention may be required");
                RaiseStatusChanged_NoLock();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop roof during safety watchdog trigger");
        }
    }


    /// <summary>
    /// Finalizer (destructor) ensures cleanup of resources if Dispose is not called.
    /// Should rarely be needed as proper disposal should occur through IAsyncDisposable.
    /// </summary>
    ~RoofControllerServiceV4()
    {
        // Pass false because we're in the finalizer
        Dispose(false);
    }

    /// <summary>
    /// Core async disposal implementation that handles cleanup of all resources.
    /// This is the primary disposal logic used by both sync and async disposal paths.
    /// </summary>
    /// <returns>A ValueTask representing the async disposal operation.</returns>
    protected virtual ValueTask DisposeAsyncCore()
    {
        if (_disposed) return ValueTask.CompletedTask;

        try
        {
            // 1. Stop any ongoing operations first
            InternalStop(RoofControllerStopReason.SystemDisposal);

            // 2. Unsubscribe from HAT events
            try
            {
                if (_hatIn1Handler is not null) _fourRelayFourInputHat.DigitalInput1Changed -= _hatIn1Handler;
                if (_hatIn2Handler is not null) _fourRelayFourInputHat.DigitalInput2Changed -= _hatIn2Handler;
                if (_hatIn3Handler is not null) _fourRelayFourInputHat.DigitalInput3Changed -= _hatIn3Handler;
                if (_hatIn4Handler is not null) _fourRelayFourInputHat.DigitalInput4Changed -= _hatIn4Handler;
                _hatIn1Handler = _hatIn2Handler = _hatIn3Handler = _hatIn4Handler = null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unsubscribing from HAT input events");
            }

            // 3. Stop and dispose the safety watchdog timer
            try
            {
                lock (_syncLock)
                {
                    if (_safetyWatchdogTimer != null)
                    {
                        _safetyWatchdogTimer.Stop();
                        _safetyWatchdogTimer.Elapsed -= SafetyWatchdog_Elapsed;
                        _safetyWatchdogTimer.Dispose();
                        _safetyWatchdogTimer = null;
                    }
                    if (_periodicVerificationTimer != null)
                    {
                        _periodicVerificationTimer.Stop();
                        _periodicVerificationTimer.Elapsed -= PeriodicVerification_Elapsed;
                        _periodicVerificationTimer.Dispose();
                        _periodicVerificationTimer = null;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing safety watchdog timer");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during async disposal of RoofController");
            throw;
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Asynchronously releases all resources used by the RoofController.
    /// This is the preferred disposal method as it properly handles async cleanup of GPIO resources.
    /// </summary>
    /// <returns>A ValueTask representing the async disposal operation.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        await DisposeAsyncCore().ConfigureAwait(false);
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Protected disposal method that implements the dispose pattern.
    /// This method handles the actual cleanup work for both disposal paths.
    /// </summary>
    /// <param name="disposing">
    /// True when called from IDisposable.Dispose, false when called from finalizer.
    /// When false, only cleanup unmanaged resources.
    /// </param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            try
            {
                // Use the async disposal pattern but block on it
                // This is acceptable in disposal path since we're already blocking
                DisposeAsyncCore().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during disposal of RoofController");
                throw;
            }
        }

        _disposed = true;
    }

    /// <summary>
    /// Synchronously releases all resources used by the RoofController.
    /// This method blocks while waiting for async operations to complete.
    /// Consider using DisposeAsync for better performance.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Helper method to throw ObjectDisposedException if this instance has been disposed.
    /// Uses volatile read for thread safety.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown if the instance has been disposed.</exception>
    protected virtual void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);
    }

    // Removed service-level polling; events now forwarded from HAT

    protected virtual void UpdateRoofStatus(bool forceRead = false)
    {
        lock (this._syncLock)
        {
            bool needRead = forceRead || !(InputsEventsActive && _lastIn1.HasValue && _lastIn2.HasValue);
            if (needRead) ForceReadInputs_NoLock();

            // Translate raw electrical levels to logical limit states based on polarity configuration.
            // Logical value (openTriggered/closedTriggered) is TRUE when the corresponding limit is reached regardless of physical wiring.
            bool rawForward = _lastIn1 ?? (_roofControllerOptions.UseNormallyClosedLimitSwitches ? true : false); // assume normal state
            bool rawReverse = _lastIn2 ?? (_roofControllerOptions.UseNormallyClosedLimitSwitches ? true : false);
            bool openTriggered = _roofControllerOptions.UseNormallyClosedLimitSwitches ? !rawForward : rawForward;
            bool closedTriggered = _roofControllerOptions.UseNormallyClosedLimitSwitches ? !rawReverse : rawReverse;

            if (openTriggered && !closedTriggered)
            {
                SetStatus(RoofControllerStatus.Open);
            }
            else if (!openTriggered && closedTriggered)
            {
                SetStatus(RoofControllerStatus.Closed);
            }
            else if (!openTriggered && !closedTriggered)
            {
                // Roof is between positions - determine based on current status and whether operation is active
                var isOperationActive = _safetyWatchdogTimer?.Enabled ?? false;

                if (this.Status == RoofControllerStatus.Opening || _lastCommandIntent == RoofControllerCommandIntent.Open)
                {
                    if (isOperationActive)
                    {
                        // Operation is still active - keep status as Opening
                        // Don't change to PartiallyOpen until the operation actually stops
                        this._logger.LogTrace("Roof opening in progress - keeping Opening status (watchdog active)");
                    }
                    else
                    {
                        // Operation stopped - roof is partially open
                        SetStatus(RoofControllerStatus.PartiallyOpen);
                        this._logger.LogDebug("Roof opening operation stopped - setting to PartiallyOpen");
                    }
                }
                else if (this.Status == RoofControllerStatus.Closing || _lastCommandIntent == RoofControllerCommandIntent.Close)
                {
                    if (isOperationActive)
                    {
                        // Operation is still active - keep status as Closing
                        // Don't change to PartiallyClose until the operation actually stops
                        this._logger.LogTrace("Roof closing in progress - keeping Closing status (watchdog active)");
                    }
                    else
                    {
                        // Operation stopped - roof is partially closed
                        SetStatus(RoofControllerStatus.PartiallyClose);
                        this._logger.LogDebug("Roof closing operation stopped - setting to PartiallyClose");
                    }
                }
                else
                {
                    // Unknown state - default to stopped
                    SetStatus(RoofControllerStatus.Stopped);
                }
            }
            else if (openTriggered && closedTriggered)
            {
                // Error state - both switches triggered simultaneously
                SetStatus(RoofControllerStatus.Error);
                this._logger.LogError("Both limit switches are triggered simultaneously - this indicates a hardware problem");
            }

            this._logger.LogDebug("UpdateRoofStatus: OpenTriggered={openTriggered}, ClosedTriggered={closedTriggered}, LastIntent={lastIntent}, Status={status}",
                openTriggered, closedTriggered, _lastCommandIntent, this.Status);

            // Update indicator LEDs to reflect current limit & fault states
            UpdateIndicatorLeds_NoLock();
            RaiseStatusChanged_NoLock();
        }
    }

    private void SetStatus(RoofControllerStatus newStatus)
    {
        if (this.Status != newStatus)
        {
            this.Status = newStatus;
            LastTransitionUtc = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Internal test helper to force a status refresh without needing reflection in tests.
    /// </summary>
    /// <param name="forceHardwareRead">If true, bypasses cached values and performs a hardware read.</param>
    internal void ForceStatusRefresh(bool forceHardwareRead = false) => UpdateRoofStatus(forceHardwareRead);

    public void RefreshStatus(bool forceHardwareRead = false)
    {
        UpdateRoofStatus(forceHardwareRead);
    }

    /// <summary>
    /// Updates HAT LEDs (LED1=open limit, LED2=closed limit, LED3=fault) with minimal I2C traffic.
    /// Assumes caller holds <see cref="_syncLock"/>.
    /// </summary>
    private void UpdateIndicatorLeds_NoLock()
    {
        try
        {
            // Display LEDs when logical limit ACTIVE (true = at limit) regardless of polarity selection
            bool rawForward = _lastIn1 ?? (_roofControllerOptions.UseNormallyClosedLimitSwitches ? true : false);
            bool rawReverse = _lastIn2 ?? (_roofControllerOptions.UseNormallyClosedLimitSwitches ? true : false);
            bool openLimit = _roofControllerOptions.UseNormallyClosedLimitSwitches ? !rawForward : rawForward;
            bool closedLimit = _roofControllerOptions.UseNormallyClosedLimitSwitches ? !rawReverse : rawReverse;
            bool fault = _lastIn3 ?? false;          // Fault input

            byte mask = 0;
            if (openLimit) mask |= 0x01;     // LED1
            if (closedLimit) mask |= 0x02;   // LED2
            if (fault) mask |= 0x04;         // LED3

            if (_lastIndicatorLedMask.HasValue && _lastIndicatorLedMask.Value == mask)
                return; // No change

            var result = _fourRelayFourInputHat.SetLedsMask(mask);
            if (!result.IsSuccessful)
            {
                if (result.Error is not null)
                    _logger.LogDebug(result.Error, "UpdateIndicatorLeds: failed to set LED mask {Mask}", mask);
                else
                    _logger.LogDebug("UpdateIndicatorLeds: failed to set LED mask {Mask} (unknown error)", mask);
            }
            else
            {
                _lastIndicatorLedMask = mask;
                _logger.LogTrace("Indicator LEDs updated - Open:{Open} Closed:{Closed} Fault:{Fault} Mask:0x{Mask:X2}", openLimit, closedLimit, fault, mask);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "UpdateIndicatorLeds: exception while updating LEDs");
        }
    }


    public virtual Result<RoofControllerStatus> Stop(RoofControllerStopReason reason = RoofControllerStopReason.NormalStop)
        {
            try
            {
                ThrowIfDisposed();

                lock (this._syncLock)
                {
                    if (this.IsInitialized == false)
                    {
                        return Result<RoofControllerStatus>.Failure(new InvalidOperationException("Device not initialized"));
                    }

                    // Preserve the previous command for status determination, only set to "Stop" if it was empty/unknown
                    if (_lastCommandIntent == RoofControllerCommandIntent.None || _lastCommandIntent == RoofControllerCommandIntent.Initialize)
                    {
                        _lastCommandIntent = RoofControllerCommandIntent.Stop;
                    }
                    // Preserve Open/Close command; InternalStop stops watchdog & updates status.
                    this.InternalStop(reason);
                    
                    this._logger.LogInformation("Stop Executed TimeUtc={TimeUtc} Reason={Reason} Status={Status}", DateTime.UtcNow.ToString("O"), reason, this.Status);
                    return Result<RoofControllerStatus>.Success(this.Status);
                }
            }
            catch (Exception ex)
            {
                return Result<RoofControllerStatus>.Failure(ex);
            }
        }

    protected virtual void InternalStop(RoofControllerStopReason reason = RoofControllerStopReason.None)
    {
        lock (this._syncLock)
        {
            // Stop watchdog first so partial states are computed immediately
            StopSafetyWatchdog_NoLock();
            // Set the last stop reason for external access
            this.LastStopReason = reason;

            // DON'T set status to Stopped here - let UpdateRoofStatus determine the correct status
            this._logger.LogInformation("InternalStop Start TimeUtc={TimeUtc} Reason={Reason} Status={Status}", DateTime.UtcNow.ToString("O"), reason, this.Status);

            // Set all relays to safe state for STOP operation atomically
            // stopRelay=true engages the stop; open/close relays are de-energized
            SetRelayStatesAtomically(
                stopRelay: true,
                openRelay: false,
                closeRelay: false
            );


            // Update status based on limit switch states and last command
            this.UpdateRoofStatus();
            this._logger.LogInformation("InternalStop Complete TimeUtc={TimeUtc} Reason={Reason} FinalStatus={Status}", DateTime.UtcNow.ToString("O"), reason, this.Status);
        }
    }

    public virtual Result<RoofControllerStatus> Open()
    {
        try
        {
            ThrowIfDisposed();

            lock (this._syncLock)
            {
                if (this.IsInitialized == false)
                {
                    return Result<RoofControllerStatus>.Failure(new InvalidOperationException("Device not initialized"));
                }

                // Refuse movement when a fault is active
                if (IsFaultActive())
                {
                    _logger.LogWarning("Open command refused: fault is active");
                    return Result<RoofControllerStatus>.Failure(new InvalidOperationException("Cannot open while a fault is active. Clear fault first."));
                }

                // Set the command BEFORE calling Stop() so UpdateRoofStatus has the correct context
                this._lastCommandIntent = RoofControllerCommandIntent.Open;

                // Always stop the current action before starting a new one.
                var stopResult = this.Stop();
                if (!stopResult.IsSuccessful)
                {
                    return stopResult;
                }

                // Single read to check limit states and handle the both-limits error case
                var (forwardLimit, reverseLimit) = GetCurrentLimitStates(forceHardwareRead: true);
                if (forwardLimit && reverseLimit)
                {
                    if (this.Status != RoofControllerStatus.Error)
                    {
                        this.Status = RoofControllerStatus.Error;
                        LastTransitionUtc = DateTimeOffset.UtcNow;
                    }
                    _logger.LogError("Open command refused: both limit switches are active");
                    return Result<RoofControllerStatus>.Failure(new InvalidOperationException("Both limit switches are active"));
                }

                if ((this.Status == RoofControllerStatus.Open) || forwardLimit)
                {
                    if (forwardLimit)
                    {
                        this._logger.LogInformation("Open command: forward/open limit is active, roof is fully open");
                    }

                    // If already open, just return
                    if (this.Status != RoofControllerStatus.Open)
                    {
                        this.Status = RoofControllerStatus.Open;
                        LastTransitionUtc = DateTimeOffset.UtcNow;
                    }

                    this._logger.LogInformation("Open Command Ignored AlreadyOpen TimeUtc={TimeUtc} Status={Status}", DateTime.UtcNow.ToString("O"), this.Status);
                    return Result<RoofControllerStatus>.Success(this.Status);
                }


                // Start the motors to open the roof atomically
                // stopRelay=false releases stop; openRelay=true energizes open; closeRelay=false
                SetRelayStatesAtomically(
                    stopRelay: false,
                    openRelay: true,
                    closeRelay: false
                );

                // Set the status to opening
                if (this.Status != RoofControllerStatus.Opening)
                {
                    this.Status = RoofControllerStatus.Opening;
                    LastTransitionUtc = DateTimeOffset.UtcNow;
                }
                // _lastCommand already set earlier before calling Stop()
                this.StartSafetyWatchdog();

                this._logger.LogInformation("Open Command Started TimeUtc={TimeUtc} Status={Status}", DateTime.UtcNow.ToString("O"), this.Status);

                return Result<RoofControllerStatus>.Success(this.Status);
            }
        }
        catch (Exception ex)
        {
            return Result<RoofControllerStatus>.Failure(ex);
        }
    }

    public virtual Result<RoofControllerStatus> Close()
    {
        try
        {
            ThrowIfDisposed();

            lock (this._syncLock)
            {
                if (this.IsInitialized == false)
                {
                    return Result<RoofControllerStatus>.Failure(new InvalidOperationException("Device not initialized"));
                }

                // Refuse movement when a fault is active
                if (IsFaultActive())
                {
                    _logger.LogWarning("Close command refused: fault is active");
                    return Result<RoofControllerStatus>.Failure(new InvalidOperationException("Cannot close while a fault is active. Clear fault first."));
                }

                // Set the command BEFORE calling Stop() so UpdateRoofStatus has the correct context
                this._lastCommandIntent = RoofControllerCommandIntent.Close;

                // Always stop the current action before starting a new one.
                var stopResult = this.Stop();
                if (!stopResult.IsSuccessful)
                {
                    return stopResult;
                }

                // Single read to check limit states and handle the both-limits error case
                var (forwardLimit, reverseLimit) = GetCurrentLimitStates(forceHardwareRead: true);
                if (forwardLimit && reverseLimit)
                {
                    if (this.Status != RoofControllerStatus.Error)
                    {
                        this.Status = RoofControllerStatus.Error;
                        LastTransitionUtc = DateTimeOffset.UtcNow;
                    }
                    _logger.LogError("Close command refused: both limit switches are active");
                    return Result<RoofControllerStatus>.Failure(new InvalidOperationException("Both limit switches are active"));
                }

                if ((this.Status == RoofControllerStatus.Closed) || reverseLimit)
                {
                    if (reverseLimit)
                    {
                        this._logger.LogInformation("Close command: reverse/closed limit is active, roof is fully closed");
                    }

                    // If already closed, just return
                    if (this.Status != RoofControllerStatus.Closed)
                    {
                        this.Status = RoofControllerStatus.Closed;
                        LastTransitionUtc = DateTimeOffset.UtcNow;
                    }

                    this._logger.LogInformation("Close Command Ignored AlreadyClosed TimeUtc={TimeUtc} Status={Status}", DateTime.UtcNow.ToString("O"), this.Status);
                    return Result<RoofControllerStatus>.Success(this.Status);
                }

                // Start the motors to close the roof atomically
                // stopRelay=false releases stop; closeRelay=true energizes close; openRelay=false
                SetRelayStatesAtomically(
                    stopRelay: false,
                    openRelay: false,
                    closeRelay: true
                );


                // Set the status to closing
                if (this.Status != RoofControllerStatus.Closing)
                {
                    this.Status = RoofControllerStatus.Closing;
                    LastTransitionUtc = DateTimeOffset.UtcNow;
                }
                // _lastCommand already set earlier before calling Stop()
                this.StartSafetyWatchdog();

                this._logger.LogInformation("Close Command Started TimeUtc={TimeUtc} Status={Status}", DateTime.UtcNow.ToString("O"), this.Status);

                return Result<RoofControllerStatus>.Success(this.Status);
            }
        }
        catch (Exception ex)
        {
            return Result<RoofControllerStatus>.Failure(ex);
        }
    }


    /// <summary>
    /// Safely sets all relay pins to the specified states atomically.
    /// This prevents hardware from being in inconsistent states due to exceptions.
    /// </summary>
    /// <param name="stopRelay">State for stop relay</param>
    /// <param name="openRelay">State for open relay</param>
    /// <param name="closeRelay">State for close relay</param>
    protected virtual void SetRelayStatesAtomically(bool stopRelay, bool openRelay, bool closeRelay)
    {
        if (this._fourRelayFourInputHat == null || _roofControllerOptions == null)
            return;

        // Guard: never energize both Open and Close simultaneously; enforce safe STOP state
        if (openRelay && closeRelay)
        {
            _logger.LogError("Invalid relay request: both Open and Close requested true. Forcing STOP state.");
            stopRelay = true;
            openRelay = false;
            closeRelay = false;
        }

        // Collect all pin operations to perform atomically
        var pinOperations = new List<(int realayId, bool relayValue, string relayName)>
            {
                (_roofControllerOptions.StopRelayId, stopRelay, "Stop"),
                (_roofControllerOptions.OpenRelayId, openRelay, "Open"),
                (_roofControllerOptions.CloseRelayId, closeRelay, "Close")
            };

        // Apply all operations or log failures individually to prevent partial states
        foreach (var (relayId, relayValue, relayName) in pinOperations)
        {
            try
            {
                var result = _fourRelayFourInputHat.TrySetRelayWithRetry(relayId, relayValue);
                if (result.IsFailure || result.Value == false)
                {
                    if (result.IsFailure && result.Error is not null)
                    {
                        _logger.LogError(result.Error, "Failed to set {RelayName} relay pin {Pin} to {Value} ", relayName, relayId, relayValue);
                    }
                    else
                    {
                        _logger.LogError("Failed to verify {RelayName} relay pin {Pin} to {Value}", relayName, relayId, relayValue);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set {RelayName} relay pin {Pin} to {Value}", relayName, relayId, relayValue);
            }
        }
    }

    public virtual async Task<Result<bool>> ClearFault(int pulseMs = 250, CancellationToken cancellationToken = default)
    {
        pulseMs = Math.Max(0, pulseMs);
        try
        {
            ThrowIfDisposed();
            int clearFaultRelayId;
            lock (this._syncLock)
            {
                if (this.IsInitialized == false)
                {
                    return Result<bool>.Failure(new InvalidOperationException("Device not initialized"));
                }
                InternalStop(RoofControllerStopReason.EmergencyStop);
                clearFaultRelayId = this._roofControllerOptions.ClearFault;
                _fourRelayFourInputHat.TrySetRelayWithRetry(clearFaultRelayId, false);
                _fourRelayFourInputHat.TrySetRelayWithRetry(clearFaultRelayId, true);
            }
            if (pulseMs > 0)
            {
                try { await Task.Delay(pulseMs, cancellationToken).ConfigureAwait(false); }
                catch (TaskCanceledException) { }
            }
            _fourRelayFourInputHat.TrySetRelayWithRetry(clearFaultRelayId, false);
            _logger.LogInformation("====ClearFault (async) - {Time}. PulseMs={PulseMs} Status={Status}", DateTime.Now.ToString("O"), pulseMs, Status);
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(ex);
        }
    }

    /// <summary>
    /// Helper to get the immediate view of forward/reverse limits using cached events when present,
    /// otherwise performing a single read from the HAT.
    /// </summary>
    protected virtual (bool forward, bool reverse) GetCurrentLimitStates(bool forceHardwareRead = false)
    {
        lock (_syncLock)
        {
            if (forceHardwareRead || !(InputsEventsActive && _lastIn1.HasValue && _lastIn2.HasValue))
            {
                ForceReadInputs_NoLock();
            }
            bool rawForward = _lastIn1 ?? (_roofControllerOptions.UseNormallyClosedLimitSwitches ? true : false);
            bool rawReverse = _lastIn2 ?? (_roofControllerOptions.UseNormallyClosedLimitSwitches ? true : false);
            bool forwardLimitActive = _roofControllerOptions.UseNormallyClosedLimitSwitches ? !rawForward : rawForward;
            bool reverseLimitActive = _roofControllerOptions.UseNormallyClosedLimitSwitches ? !rawReverse : rawReverse;
            return (forwardLimitActive, reverseLimitActive);
        }
    }

    protected virtual bool IsForwardLimitActive()
    {
        var (forward, _) = GetCurrentLimitStates(forceHardwareRead: true);
        return forward; // logical active
    }

    protected virtual bool IsReverseLimitActive()
    {
        var (_, reverse) = GetCurrentLimitStates(forceHardwareRead: true);
        return reverse; // logical active
    }

    protected virtual bool IsFaultActive()
    {
        lock (_syncLock)
        {
            if (!(InputsEventsActive && _lastIn3.HasValue))
            {
                ForceReadInputs_NoLock();
            }
            return _lastIn3 ?? false;
        }
    }

    /// <summary>
    /// Thread-safe check if the controller is disposed without throwing.
    /// </summary>
    /// <returns>True if disposed, false otherwise</returns>
    protected virtual bool IsDisposed => _disposed;

    /// <summary>
    /// Performs a direct read of all digital inputs updating cached values. Caller must hold _syncLock.
    /// </summary>
    /// <returns>True if read succeeded, false otherwise.</returns>
    private bool ForceReadInputs_NoLock()
    {
        try
        {
            var inputs = _fourRelayFourInputHat.GetAllDigitalInputs();
            if (!inputs.IsFailure)
            {
                _lastIn1 = inputs.Value.in1;
                _lastIn2 = inputs.Value.in2;
                _lastIn3 = inputs.Value.in3;
                _lastIn4 = inputs.Value.in4;
                return true;
            }
            if (inputs.Error is not null)
            {
                _logger.LogWarning(inputs.Error, "ForceReadInputs: failed to read inputs; retaining last known values");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ForceReadInputs: exception while reading digital inputs");
        }
        return false;
    }

    private void RaiseStatusChanged_NoLock()
    {
        // Build snapshot while holding lock, then release before invoking handlers
        RoofStatusChangedEventArgs? args = null;
        if (StatusChanged != null)
        {
            var snapshot = new RoofStatusResponse(
                Status,
                IsMoving,
                LastStopReason,
                LastTransitionUtc,
                IsWatchdogActive,
                WatchdogSecondsRemaining,
                _lastIn4 ?? false
            );
            args = new RoofStatusChangedEventArgs(snapshot);
        }
        if (args != null)
        {
            try
            {
                // Invoke outside the lock to prevent deadlocks
                Task.Run(() =>
                {
                    try { StatusChanged?.Invoke(this, args); } catch { }
                });
            }
            catch { }
        }
    }
}
}
