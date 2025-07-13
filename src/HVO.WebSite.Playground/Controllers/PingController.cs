using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;

namespace HVO.WebSite.Playground.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class PingController : ControllerBase
{
    private readonly ILogger<PingController> _logger;

    public PingController(ILogger<PingController> logger)
    {
        _logger = logger;
    }

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
