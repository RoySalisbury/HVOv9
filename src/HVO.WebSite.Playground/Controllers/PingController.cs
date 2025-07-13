using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;

namespace HVO.WebSite.Playground.Controllers;

/// <summary>
/// API controller for health check and connectivity testing
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
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
    [HttpGet]
    public IActionResult Get()
    {
        var response = new
        {
            Message = "Pong! API is working perfectly.",
            Version = "1.0",
            Timestamp = DateTime.UtcNow,
            MachineName = Environment.MachineName
        };

        _logger.LogInformation("Ping API endpoint was called at {Timestamp}", DateTime.UtcNow);

        return Ok(response);
    }
}
