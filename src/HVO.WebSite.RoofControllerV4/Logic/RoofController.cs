using System.Device.Gpio;
using HVO.Iot.Devices;
using Microsoft.Extensions.Options;

namespace HVO.WebSite.RoofControllerV4.Logic
{
    public sealed class RoofController : IRoofController, IAsyncDisposable, IDisposable
    {
        private readonly ILogger<RoofController> _logger;
        private readonly RoofControllerOptions _roofControllerOptions;

        private GpioController _gpioController;
        private readonly ILogger<GpioLimitSwitch> _limitSwitchLogger;
        private readonly GpioLimitSwitch _roofOpenLimitSwitch;
        private readonly GpioLimitSwitch _roofClosedLimitSwitch;

        private readonly object _syncLock = new object();
        private bool _disposed;

        public RoofController(ILogger<RoofController> logger, IOptions<RoofControllerOptions> roofControllerOptions, GpioController gpioController)
        {
            ArgumentNullException.ThrowIfNull(gpioController);

            this._logger = logger;
            this._limitSwitchLogger = LoggerFactory.Create(o => { }).CreateLogger<GpioLimitSwitch>();

            this._roofControllerOptions = roofControllerOptions.Value;

            this._gpioController = gpioController;
            this._roofOpenLimitSwitch = new GpioLimitSwitch(this._gpioController, this._roofControllerOptions.RoofOpenedLimitSwitchPin, true, false, _roofControllerOptions.LimitSwitchDebounce, this._limitSwitchLogger);
            this._roofClosedLimitSwitch = new GpioLimitSwitch(this._gpioController, this._roofControllerOptions.RoofClosedLimitSwitchPin, true, false, _roofControllerOptions.LimitSwitchDebounce, this._limitSwitchLogger);

        }

        public bool IsInitialized { get; private set; } = false;

        public RoofControllerStatus Status { get; private set; } = RoofControllerStatus.NotInitialized;


        public Task<bool> Initialize(CancellationToken cancellationToken)
        {
            if (this._disposed)
            {
                throw new ObjectDisposedException(nameof(RoofController));
            }

            lock (this._syncLock)
            {
                if (this.IsInitialized)
                {
                    throw new Exception("Already Initialized");
                }

                // Setup the cancellation token registration so we know when things are shutting down as soon as possible and can call STOP.
                cancellationToken.Register(() => this.Stop());

                // Always reset to a known safe state on initialization.
                this.Stop();

                // Setup the GPIO controller
                try
                {
                    this._roofOpenLimitSwitch.LimitSwitchTriggered += roofOpenLimitSwitch_LimitSwitchTriggered;
                    this._roofClosedLimitSwitch.LimitSwitchTriggered += roofClosedLimitSwitch_LimitSwitchTriggered;

                }
                catch
                {
                    this._roofOpenLimitSwitch?.LimitSwitchTriggered -= roofOpenLimitSwitch_LimitSwitchTriggered;
                    this._roofClosedLimitSwitch.LimitSwitchTriggered -= roofClosedLimitSwitch_LimitSwitchTriggered;

                    this.IsInitialized = true;
                    throw;
                }

                this.IsInitialized = true;
                return Task.FromResult(this.IsInitialized);
            }
        }

        private void roofOpenLimitSwitch_LimitSwitchTriggered(object? sender, LimitSwitchTriggeredEventArgs e)
        {
            this._logger.LogInformation("RoofOpenLimitSwitch: {changeType} - {eventDateTime}", e.ChangeType, e.EventDateTime);

            if (e.ChangeType == PinEventTypes.Falling)
            {
                this.Stop();
                this.Status = RoofControllerStatus.Open; 
            }
            else
            {
                this.Status = RoofControllerStatus.Opening;
            }
        }

        private void roofClosedLimitSwitch_LimitSwitchTriggered(object? sender, LimitSwitchTriggeredEventArgs e)
        {
            this._logger.LogInformation("RoofClosedLimitSwitch: {changeType} - {eventDateTime}", e.ChangeType, e.EventDateTime);

            if (e.ChangeType == PinEventTypes.Falling)
            {
                this.Stop();
                this.Status = RoofControllerStatus.Closed;
            }
            else
            {
                this.Status = RoofControllerStatus.Closing;
            }
        }

        public void Stop()
        {
            ThrowIfDisposed();

            lock (this._syncLock)
            {
                if (this.IsInitialized == false)
                {
                    throw new Exception("Device not initialized");
                }

                this.InternalStop();
                this._logger.LogInformation($"====Stop - {DateTime.Now:O}. Current Status: {this.Status}");
            }
        }

        private void InternalStop()
        {
            lock (this._syncLock)
            {
                if (this.IsInitialized == false)
                {
                    throw new Exception("Device not initialized");
                }

                this.Status = RoofControllerStatus.Stopped;
                this._logger.LogInformation($"====InternalStop - {DateTime.Now:O}. Current Status: {this.Status}");
            }

        }

        public void Open()
        {
            ThrowIfDisposed();

            lock (this._syncLock)
            {
                if (this.IsInitialized == false)
                {
                    throw new Exception("Device not initialized");
                }

                this.Status = RoofControllerStatus.Opening;
                this._logger.LogInformation($"====Open - {DateTime.Now:O}. Current Status: {this.Status}");
            }
        }

        public void Close()
        {
            ThrowIfDisposed();

            lock (this._syncLock)
            {
                if (this.IsInitialized == false)
                {
                    throw new Exception("Device not initialized");
                }

                this.Status = RoofControllerStatus.Closed;

                this._logger.LogInformation($"====Close - {DateTime.Now:O}. Current Status: {this.Status}");
            }
        }


        /// <summary>
        /// Finalizer (destructor) ensures cleanup of resources if Dispose is not called.
        /// Should rarely be needed as proper disposal should occur through IAsyncDisposable.
        /// </summary>
        ~RoofController()
        {
            // Pass false because we're in the finalizer
            Dispose(false);
        }

        /// <summary>
        /// Core async disposal implementation that handles cleanup of all resources.
        /// This is the primary disposal logic used by both sync and async disposal paths.
        /// </summary>
        /// <returns>A ValueTask representing the async disposal operation.</returns>
        private async ValueTask DisposeAsyncCore()
        {
            if (_disposed) return;

            try
            {
                // 1. Stop any ongoing operations first
                InternalStop();

                if (IsInitialized)
                {
                    // 2. Clean up the open limit switch
                    if (_roofOpenLimitSwitch != null)
                    {
                        // Unregister event handler before disposal to prevent potential callbacks
                        _roofOpenLimitSwitch.LimitSwitchTriggered -= roofOpenLimitSwitch_LimitSwitchTriggered;
                        await _roofOpenLimitSwitch.DisposeAsync().ConfigureAwait(false);
                    }

                    // 3. Clean up the closed limit switch
                    if (_roofClosedLimitSwitch != null)
                    {
                        // Unregister event handler before disposal to prevent potential callbacks
                        _roofClosedLimitSwitch.LimitSwitchTriggered -= roofClosedLimitSwitch_LimitSwitchTriggered;
                        await _roofClosedLimitSwitch.DisposeAsync().ConfigureAwait(false);
                    }

                    // 4. Finally, clean up the GPIO controller
                    if (_gpioController != null)
                    {
                        // Use async disposal if available, otherwise fall back to sync
                        if (_gpioController is IAsyncDisposable asyncDisposable)
                        {
                            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                        }
                        else
                        {
                            _gpioController.Dispose();
                        }
                        _gpioController = null!;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during async disposal of RoofController");
                throw;
            }
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
        private void Dispose(bool disposing)
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
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown if the instance has been disposed.</exception>
        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);
        }
    }
}