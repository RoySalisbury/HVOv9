using Microsoft.Extensions.Options;
using HVO.Iot.Devices.Abstractions;
using HVO.Iot.Devices;
using System.Device.Gpio;
using System.Timers;

namespace HVO.WebSite.RoofControllerV4.Logic
{
    /// <summary>
    /// A derived implementation of RoofControllerService that demonstrates inheritance.
    /// This class can be extended with custom functionality while maintaining all base functionality.
    /// </summary>
    public class RoofControllerServiceWithSimulatedEvents : RoofControllerService
    {
        #region Simulation Timer Fields
        
        private System.Timers.Timer? _simulationTimer;
        private RoofControllerStatus _expectedCompletionStatus;

        #endregion

        /// <summary>
        /// Initializes a new instance of the RoofControllerServiceWithSimulatedEvents class.
        /// </summary>
        /// <param name="logger">The logger instance for this service.</param>
        /// <param name="roofControllerOptions">The configuration options for the roof controller.</param>
        /// <param name="gpioController">The GPIO controller instance.</param>
        public RoofControllerServiceWithSimulatedEvents(
            ILogger<RoofControllerService> logger, 
            IOptions<RoofControllerOptions> roofControllerOptions, 
            IGpioController gpioController) 
            : base(logger, roofControllerOptions, gpioController)
        {
            _logger.LogInformation("RoofControllerServiceWithSimulatedEvents initialized");
        }

        /// <summary>
        /// Initializes the roof controller and sets the default simulated state to closed.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation</param>
        /// <returns>Result indicating success or failure of initialization</returns>
        public override Task<Result<bool>> Initialize(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting RoofControllerServiceWithSimulatedEvents initialization");
            
            // Call the base class initialization first
            var result = base.Initialize(cancellationToken);
            
            if (result.Result.IsSuccessful)
            {
                // Set the default simulated state: roof closed
                _logger.LogInformation("Setting default simulated state: roof closed");
                _lastCommand = "Close";
                Status = RoofControllerStatus.Closed;
                
                // Initialize limit switches in simulation mode with correct initial states
                // Roof starts closed, so closed limit switch should be triggered
                if (_roofClosedLimitSwitch != null)
                {
                    _roofClosedLimitSwitch.SetSimulationMode(true);  // Closed limit switch is triggered
                    _logger.LogInformation("Closed limit switch initialized in simulation mode: triggered = true");
                }
                else
                {
                    _logger.LogWarning("Closed limit switch is null - cannot initialize simulation mode");
                }
                
                if (_roofOpenLimitSwitch != null)
                {
                    _roofOpenLimitSwitch.SetSimulationMode(false);   // Open limit switch is not triggered
                    _logger.LogInformation("Open limit switch initialized in simulation mode: triggered = false");
                }
                else
                {
                    _logger.LogWarning("Open limit switch is null - cannot initialize simulation mode");
                }
                
                _logger.LogInformation("Simulated state initialized - Roof status set to: {status}, Closed limit switch: triggered, Open limit switch: not triggered", Status);
            }
            else
            {
                _logger.LogError("Base class initialization failed - simulation state not initialized");
            }
            
            return result;
        }

        #region Simulation Timer Properties

        /// <summary>
        /// Gets a value indicating whether the simulation timer is currently running.
        /// </summary>
        public bool IsSimulationTimerRunning => _simulationTimer != null && _simulationTimer.Enabled;

        /// <summary>
        /// Gets the remaining time for the simulation timer in seconds.
        /// </summary>
        public double SimulationTimeRemaining
        {
            get
            {
                if (!IsSimulationTimerRunning || _operationStartTime == DateTime.MinValue)
                    return 0;

                var elapsed = DateTime.Now - _operationStartTime;
                var remaining = _roofControllerOptions.SimulationTimeout - elapsed;
                return Math.Max(0, remaining.TotalSeconds);
            }
        }

        #endregion

        #region Simulation Timer Management

        /// <summary>
        /// Starts the simulation timer for automatic limit switch triggering.
        /// </summary>
        /// <param name="expectedStatus">The expected roof status when the timer completes</param>
        private void StartSimulationTimer(RoofControllerStatus expectedStatus)
        {
            try
            {
                StopSimulationTimer();

                _expectedCompletionStatus = expectedStatus;
                _operationStartTime = DateTime.Now;

                // Use the configured simulation timeout (defaults to 80% of safety watchdog timeout)
                var simulationTimeoutMs = _roofControllerOptions.SimulationTimeout.TotalMilliseconds;
                _simulationTimer = new System.Timers.Timer(simulationTimeoutMs);
                _simulationTimer.AutoReset = false;
                _simulationTimer.Elapsed += OnSimulationTimerElapsed;
                _simulationTimer.Start();

                _logger.LogInformation("Simulation timer started - roof will automatically reach {expectedStatus} position in {timeoutSeconds} seconds", 
                    expectedStatus, _roofControllerOptions.SimulationTimeout.TotalSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting simulation timer");
            }
        }

        /// <summary>
        /// Stops the simulation timer.
        /// </summary>
        private void StopSimulationTimer()
        {
            try
            {
                if (_simulationTimer != null)
                {
                    _simulationTimer.Stop();
                    _simulationTimer.Elapsed -= OnSimulationTimerElapsed;
                    _simulationTimer.Dispose();
                    _simulationTimer = null;
                    _logger.LogInformation("Simulation timer stopped");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping simulation timer");
            }
        }

        /// <summary>
        /// Handles the simulation timer elapsed event.
        /// </summary>
        private void OnSimulationTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            try
            {
                _logger.LogInformation("Simulation timer triggered - roof automatically reached {expectedStatus} position", _expectedCompletionStatus);

                if (_expectedCompletionStatus == RoofControllerStatus.Open)
                {
                    // Trigger the open limit switch
                    SimulateOpenLimitSwitchTriggered();
                }
                else if (_expectedCompletionStatus == RoofControllerStatus.Closed)
                {
                    // Trigger the closed limit switch
                    SimulateClosedLimitSwitchTriggered();
                }

                StopSimulationTimer();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in simulation timer elapsed handler");
            }
        }

        #endregion

        #region Overridden Methods with Simulation Timer

        /// <summary>
        /// Opens the roof and starts the simulation timer.
        /// </summary>
        /// <returns>Result indicating the success or failure of the operation</returns>
        public override Result<RoofControllerStatus> Open()
        {
            var result = base.Open();
            
            if (result.IsSuccessful)
            {
                // Start the simulation timer to automatically trigger the open limit switch
                StartSimulationTimer(RoofControllerStatus.Open);
            }
            
            return result;
        }

        /// <summary>
        /// Closes the roof and starts the simulation timer.
        /// </summary>
        /// <returns>Result indicating the success or failure of the operation</returns>
        public override Result<RoofControllerStatus> Close()
        {
            var result = base.Close();
            
            if (result.IsSuccessful)
            {
                // Start the simulation timer to automatically trigger the closed limit switch
                StartSimulationTimer(RoofControllerStatus.Closed);
            }
            
            return result;
        }

        /// <summary>
        /// Stops the roof and stops any running simulation timer.
        /// </summary>
        /// <returns>Result indicating the success or failure of the operation</returns>
        public override Result<RoofControllerStatus> Stop()
        {
            StopSimulationTimer();
            return base.Stop();
        }

        #endregion

        // This derived class inherits all functionality from RoofControllerService
        // Custom methods and overrides can be added here as needed

        #region Simulation Methods for Testing

        /// <summary>
        /// Simulates pressing the roof open button.
        /// </summary>
        public void SimulateOpenButtonDown()
        {
            _logger.LogInformation("Simulating open button down event");
            roofOpenButton_OnButtonDown(this, EventArgs.Empty);
        }

        /// <summary>
        /// Simulates releasing the roof open button.
        /// </summary>
        public void SimulateOpenButtonUp()
        {
            _logger.LogInformation("Simulating open button up event");
            roofOpenButton_OnButtonUp(this, EventArgs.Empty);
        }

        /// <summary>
        /// Simulates pressing the roof close button.
        /// </summary>
        public void SimulateCloseButtonDown()
        {
            _logger.LogInformation("Simulating close button down event");
            roofCloseButton_OnButtonDown(this, EventArgs.Empty);
        }

        /// <summary>
        /// Simulates releasing the roof close button.
        /// </summary>
        public void SimulateCloseButtonUp()
        {
            _logger.LogInformation("Simulating close button up event");
            roofCloseButton_OnButtonUp(this, EventArgs.Empty);
        }

        /// <summary>
        /// Simulates pressing the roof stop button.
        /// </summary>
        public void SimulateStopButtonDown()
        {
            _logger.LogInformation("Simulating stop button down event");
            roofStopButton_OnButtonDown(this, EventArgs.Empty);
        }

        /// <summary>
        /// Simulates the roof open limit switch being triggered (roof fully opened).
        /// </summary>
        public void SimulateOpenLimitSwitchTriggered()
        {
            _logger.LogInformation("Simulating open limit switch triggered event");
            
            // Stop the simulation timer if running (manual override)
            StopSimulationTimer();
            
            // Use the proper simulation method to update the limit switch state
            _roofOpenLimitSwitch?.SimulateTrigger(true);
        }

        /// <summary>
        /// Simulates the roof open limit switch being released.
        /// </summary>
        public void SimulateOpenLimitSwitchReleased()
        {
            _logger.LogInformation("Simulating open limit switch released event");
            
            // Use the proper simulation method to update the limit switch state
            _roofOpenLimitSwitch?.SimulateTrigger(false);
        }

        /// <summary>
        /// Simulates the roof closed limit switch being triggered (roof fully closed).
        /// </summary>
        public void SimulateClosedLimitSwitchTriggered()
        {
            _logger.LogInformation("Simulating closed limit switch triggered event");
            
            // Stop the simulation timer if running (manual override)
            StopSimulationTimer();
            
            // Use the proper simulation method to update the limit switch state
            _roofClosedLimitSwitch?.SimulateTrigger(true);
        }

        /// <summary>
        /// Simulates the roof closed limit switch being released.
        /// </summary>
        public void SimulateClosedLimitSwitchReleased()
        {
            _logger.LogInformation("Simulating closed limit switch released event");
            
            // Use the proper simulation method to update the limit switch state
            _roofClosedLimitSwitch?.SimulateTrigger(false);
        }

        #endregion

        #region Disposal

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopSimulationTimer();
            }
            base.Dispose(disposing);
        }

        #endregion
    }
}
