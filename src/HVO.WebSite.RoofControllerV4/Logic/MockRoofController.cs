using Microsoft.Extensions.Logging;
using HVO;

namespace HVO.WebSite.RoofControllerV4.Logic
{
    /// <summary>
    /// Mock implementation of IRoofController for development environments
    /// </summary>
    public class MockRoofController : IRoofController
    {
        private readonly ILogger<MockRoofController> _logger;

        public MockRoofController(ILogger<MockRoofController> logger)
        {
            _logger = logger;
            _logger.LogInformation("MockRoofController initialized for development environment");
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
            _logger.LogInformation("MockRoofController: Stop called");
            Status = RoofControllerStatus.Stopped;
            return Result<RoofControllerStatus>.Success(Status);
        }

        public Result<RoofControllerStatus> Open()
        {
            _logger.LogInformation("MockRoofController: Open called");
            Status = RoofControllerStatus.Opening;
            return Result<RoofControllerStatus>.Success(Status);
        }

        public Result<RoofControllerStatus> Close()
        {
            _logger.LogInformation("MockRoofController: Close called");
            Status = RoofControllerStatus.Closing;
            return Result<RoofControllerStatus>.Success(Status);
        }
    }
}
