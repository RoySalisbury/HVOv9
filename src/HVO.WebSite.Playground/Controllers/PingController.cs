using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using Microsoft.OpenApi.Models;
using HVO.WebSite.Playground.Models;

namespace HVO.WebSite.Playground.Controllers;

/// <summary>
/// API controller for health check and connectivity testing
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Tags("Health Check")]
public class PingController : ControllerBase
{
    private readonly ILogger<PingController> _logger;

    /// <summary>
    /// Initializes a new instance of the PingController
    /// </summary>
    /// <param name="logger">Logger for tracking ping controller operations</param>
    public PingController(ILogger<PingController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Health check endpoint that returns a simple response to verify API connectivity
    /// </summary>
    /// <returns>A response indicating the API is working with timestamp and machine info</returns>
    /// <response code="200">API is operational and responding normally</response>
    /// <response code="500">Internal server error occurred</response>
    [HttpGet("health")]
    [Produces("application/json")]
    [ProducesResponseType<PingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public IActionResult HealthCheck()
    {
        var response = new PingResponse
        {
            Message = "Pong! API is working perfectly.",
            Version = "1.0",
            Timestamp = DateTime.UtcNow,
            MachineName = Environment.MachineName
        };

        _logger.LogInformation("Health check API endpoint was called at {Timestamp}", DateTime.UtcNow);

        return Ok(response);
    }
}
