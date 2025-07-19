using Microsoft.Extensions.Logging;
using HVO;

namespace HVO.WebSite.RoofControllerV4.Logic
{
    /// <summary>
    /// Mock implementation of IRoofController for development environments
    /// Simulates realistic timing and status transitions for UI testing
    /// </summary>
    public class MockRoofController : IRoofController, IDisposable
    {
        private readonly ILogger<MockRoofController> _logger;
        private System.Timers.Timer? _operationTimer;
        private readonly TimeSpan _simulatedOperationTime = TimeSpan.FromSeconds(10);
        private readonly object _syncLock = new object();
        private bool _disposed = false;

        public MockRoofController(ILogger<MockRoofController> logger)
        {
            _logger = logger;
            _logger.LogInformation("MockRoofController initialized for development environment with realistic timing simulation");
        }

        public bool IsInitialized { get; private set; } = false;

        public RoofControllerStatus Status { get; private set; } = RoofControllerStatus.Stopped;

        public Task<Result<bool>> Initialize(CancellationToken cancellationToken)
        {
            _logger.LogInformation("MockRoofController: Initialize called");
            IsInitialized = true;
            return Task.FromResult(Result<bool>.Success(true));
        }

        public Result<RoofControllerStatus> Stop()
        {
            lock (_syncLock)
            {
                if (_disposed) return Result<RoofControllerStatus>.Failure(new ObjectDisposedException(nameof(MockRoofController)));

                _logger.LogInformation("MockRoofController: Stop called - Current Status: {CurrentStatus}", Status);
                
                // Stop any ongoing operation simulation
                StopOperationSimulation();
                
                Status = RoofControllerStatus.Stopped;
                _logger.LogInformation("MockRoofController: Stop completed - Status = {Status}", Status);
                
                return Result<RoofControllerStatus>.Success(Status);
            }
        }

        public Result<RoofControllerStatus> Open()
        {
            lock (_syncLock)
            {
                if (_disposed) return Result<RoofControllerStatus>.Failure(new ObjectDisposedException(nameof(MockRoofController)));

                _logger.LogInformation("MockRoofController: Open called - Current Status: {CurrentStatus}", Status);
                
                // If already open, just return current status
                if (Status == RoofControllerStatus.Open)
                {
                    _logger.LogInformation("MockRoofController: Already open, no action needed");
                    return Result<RoofControllerStatus>.Success(Status);
                }
                
                // Stop any current operation first
                StopOperationSimulation();
                
                // Start opening operation
                Status = RoofControllerStatus.Opening;
                StartOperationSimulation(RoofControllerStatus.Open);
                
                _logger.LogInformation("MockRoofController: Opening started - will complete in {Seconds} seconds", _simulatedOperationTime.TotalSeconds);
                return Result<RoofControllerStatus>.Success(Status);
            }
        }

        public Result<RoofControllerStatus> Close()
        {
            lock (_syncLock)
            {
                if (_disposed) return Result<RoofControllerStatus>.Failure(new ObjectDisposedException(nameof(MockRoofController)));

                _logger.LogInformation("MockRoofController: Close called - Current Status: {CurrentStatus}", Status);
                
                // If already closed, just return current status
                if (Status == RoofControllerStatus.Closed)
                {
                    _logger.LogInformation("MockRoofController: Already closed, no action needed");
                    return Result<RoofControllerStatus>.Success(Status);
                }
                
                // Stop any current operation first
                StopOperationSimulation();
                
                // Start closing operation
                Status = RoofControllerStatus.Closing;
                StartOperationSimulation(RoofControllerStatus.Closed);
                
                _logger.LogInformation("MockRoofController: Closing started - will complete in {Seconds} seconds", _simulatedOperationTime.TotalSeconds);
                return Result<RoofControllerStatus>.Success(Status);
            }
        }

        /// <summary>
        /// Starts a timer to simulate the roof operation completing after a realistic delay
        /// </summary>
        private void StartOperationSimulation(RoofControllerStatus targetStatus)
        {
            try
            {
                _operationTimer?.Stop();
                _operationTimer?.Dispose();
                
                _operationTimer = new System.Timers.Timer(_simulatedOperationTime.TotalMilliseconds);
                _operationTimer.Elapsed += (sender, e) => CompleteOperation(targetStatus);
                _operationTimer.AutoReset = false;
                _operationTimer.Start();
                
                _logger.LogDebug("MockRoofController: Operation simulation timer started for {TargetStatus}", targetStatus);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting operation simulation timer");
            }
        }

        /// <summary>
        /// Stops any ongoing operation simulation
        /// </summary>
        private void StopOperationSimulation()
        {
            try
            {
                if (_operationTimer != null)
                {
                    _operationTimer.Stop();
                    _operationTimer.Dispose();
                    _operationTimer = null;
                    _logger.LogDebug("MockRoofController: Operation simulation timer stopped");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping operation simulation timer");
            }
        }

        /// <summary>
        /// Completes the simulated operation by setting the final status
        /// </summary>
        private void CompleteOperation(RoofControllerStatus targetStatus)
        {
            lock (_syncLock)
            {
                if (_disposed) return;

                try
                {
                    var previousStatus = Status;
                    Status = targetStatus;
                    
                    _logger.LogInformation("MockRoofController: Operation completed - Status changed from {PreviousStatus} to {CurrentStatus}", 
                        previousStatus, Status);
                    
                    // Clean up the timer
                    _operationTimer?.Dispose();
                    _operationTimer = null;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error completing simulated operation");
                }
            }
        }

        /// <summary>
        /// Dispose of resources
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            lock (_syncLock)
            {
                try
                {
                    StopOperationSimulation();
                    _logger.LogInformation("MockRoofController disposed");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during MockRoofController disposal");
                }
                finally
                {
                    _disposed = true;
                }
            }

            GC.SuppressFinalize(this);
        }
    }
}
