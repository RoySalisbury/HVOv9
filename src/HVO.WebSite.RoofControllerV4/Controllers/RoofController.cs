using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using System.Device.Gpio;
using System.Device.Gpio.Drivers;
using HVO.WebSite.RoofControllerV4.Logic;

namespace HVO.WebSite.RoofControllerV4.Controllers
{
    [ApiController, ApiVersion("4.0"), Produces("application/json")]
    [Route("api/v{version:apiVersion}/RoofControl")]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public class RoofController : ControllerBase
    {
        private readonly ILogger<RoofController> _logger;
        private readonly IRoofController _roofController;

        public RoofController(ILogger<RoofController> logger, IRoofController roofController)
        {
            this._logger = logger;
            this._roofController = roofController;
        }

        [HttpGet, Route("Status", Name = nameof(GetRoofStatus))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public ActionResult<RoofControllerStatus> GetRoofStatus()
        {
            return Ok(this._roofController.Status);
        }

        [HttpGet, Route("Open", Name = nameof(DoRoofOpen))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public ActionResult<RoofControllerStatus> DoRoofOpen()
        {
            this._roofController.Open();
            return Ok(this._roofController.Status);
        }

        [HttpGet, Route("Close", Name = nameof(DoRoofClose))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public ActionResult<RoofControllerStatus> DoRoofClose()
        {
            this._roofController.Close();
            return Ok(this._roofController.Status);
        }

        [HttpGet, Route("Stop", Name = nameof(DoRoofStop))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public ActionResult<RoofControllerStatus> DoRoofStop()
        {
            this._roofController.Stop();
            return Ok(this._roofController.Status);
        }
    }
}
