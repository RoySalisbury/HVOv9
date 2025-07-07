using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using System.Device.Gpio;
using System.Device.Gpio.Drivers;

namespace HVO.WebSite.RoofControllerV4.Controllers
{
    [ApiController, ApiVersion("4.0"), Produces("application/json")]
    [Route("api/v{version:apiVersion}/RoofControl")]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public class RoofController : ControllerBase
    {
        private readonly ILogger<RoofController> _logger;
        
        public RoofController(ILogger<RoofController> logger)
        {
            _logger = logger;
        }

        [HttpGet, Route("Status", Name = nameof(GetRoofStatus))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public ActionResult<RoofControllerStatus> GetRoofStatus()
        {
            return RoofControllerStatus.Unknown;
        }
    }

    public enum RoofControllerStatus {
        Unknown = 0,
        Closed = 1,
        Open = 2,
        Opening = 3,
        Closing = 4,
    }
}
