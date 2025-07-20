using Microsoft.Extensions.Diagnostics.HealthChecks;
using HVO.WebSite.RoofControllerV4.Logic;
using HVO.WebSite.RoofControllerV4.Models;

namespace HVO.WebSite.RoofControllerV4.HealthChecks
{
    /// <summary>
    /// Health check for the roof controller system to monitor its operational status.
    /// </summary>
    public class RoofControllerHealthCheck : IHealthCheck
    {
        private readonly IRoofControllerService _roofController;
        private readonly ILogger<RoofControllerHealthCheck> _logger;

        public RoofControllerHealthCheck(IRoofControllerService roofController, ILogger<RoofControllerHealthCheck> logger)
        {
            _roofController = roofController;
            _logger = logger;
        }

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                var data = new Dictionary<string, object>
                {
                    ["IsInitialized"] = _roofController.IsInitialized,
                    ["Status"] = _roofController.Status.ToString(),
                    ["CheckTime"] = DateTime.UtcNow
                };

                // Check if the roof controller is initialized
                if (!_roofController.IsInitialized)
                {
                    _logger.LogWarning("Roof controller is not initialized");
                    return Task.FromResult(HealthCheckResult.Unhealthy(
                        "Roof controller is not initialized", 
                        null, 
                        data));
                }

                // Check if the roof controller is in an error state
                if (_roofController.Status == RoofControllerStatus.Error)
                {
                    _logger.LogError("Roof controller is in error state");
                    return Task.FromResult(HealthCheckResult.Unhealthy(
                        "Roof controller is in error state", 
                        null, 
                        data));
                }

                // Check if the roof controller is in an unknown state
                if (_roofController.Status == RoofControllerStatus.Unknown)
                {
                    _logger.LogWarning("Roof controller status is unknown");
                    return Task.FromResult(HealthCheckResult.Degraded(
                        "Roof controller status is unknown", 
                        null, 
                        data));
                }

                // All checks passed
                _logger.LogDebug("Roof controller health check passed");
                return Task.FromResult(HealthCheckResult.Healthy(
                    "Roof controller is operational", 
                    data));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during roof controller health check");
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    "Error checking roof controller health", 
                    ex));
            }
        }
    }
}
