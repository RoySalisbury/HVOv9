using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using System.Device.Gpio;
using System.Device.Gpio.Drivers;
using HVO.WebSite.RoofControllerV4.Logic;
using HVO.WebSite.RoofControllerV4.Models;

namespace HVO.WebSite.RoofControllerV4.Controllers
{
    /// <summary>
    /// Roof Controller API v4.0 - Controls the observatory roof operations
    /// </summary>
    [ApiController, ApiVersion("4.0"), Produces("application/json")]
    [Route("api/v{version:apiVersion}/RoofControl")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    [Tags("Roof Control")]
    public class RoofController : ControllerBase
    {
        private readonly ILogger<RoofController> _logger;
        private readonly IRoofControllerServiceV4 _roofController;

        public RoofController(ILogger<RoofController> logger, IRoofControllerServiceV4 roofController)
        {
            this._logger = logger;
            this._roofController = roofController;
        }

        /// <summary>
        /// Gets the current status of the roof controller
        /// </summary>
        /// <returns>Current roof controller status</returns>
        /// <response code="200">Returns the current roof status</response>
        /// <response code="500">Internal server error occurred</response>
        [HttpGet, Route("Status", Name = nameof(GetRoofStatus))]
        [ProducesResponseType(typeof(RoofControllerStatus), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
        public ActionResult<RoofControllerStatus> GetRoofStatus()
        {
            return Ok(this._roofController.Status);
        }

        /// <summary>
        /// Opens the observatory roof
        /// </summary>
        /// <returns>Updated roof controller status after opening operation</returns>
        /// <response code="200">Roof opening operation completed successfully</response>
        /// <response code="500">Internal server error or service state issue occurred</response>
        [HttpGet, Route("Open", Name = nameof(DoRoofOpen))]
        [ProducesResponseType(typeof(RoofControllerStatus), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
        public ActionResult<RoofControllerStatus> DoRoofOpen()
        {
            var result = this._roofController.Open();
            
            return result.Match(
                success: status => Ok(status),
                failure: error => error switch
                {
                    // Service state issues should return 500
                    InvalidOperationException => Problem(
                        title: "Service Error",
                        detail: error.Message,
                        statusCode: StatusCodes.Status500InternalServerError
                    ),
                    _ => Problem(
                        title: "Internal Server Error", 
                        detail: "An error occurred while opening the roof",
                        statusCode: StatusCodes.Status500InternalServerError
                    )
                }
            );
        }

        /// <summary>
        /// Closes the observatory roof
        /// </summary>
        /// <returns>Updated roof controller status after closing operation</returns>
        /// <response code="200">Roof closing operation completed successfully</response>
        /// <response code="500">Internal server error or service state issue occurred</response>
        [HttpGet, Route("Close", Name = nameof(DoRoofClose))]
        [ProducesResponseType(typeof(RoofControllerStatus), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
        public ActionResult<RoofControllerStatus> DoRoofClose()
        {
            var result = this._roofController.Close();
            
            return result.Match(
                success: status => Ok(status),
                failure: error => error switch
                {
                    // Service state issues should return 500
                    InvalidOperationException => Problem(
                        title: "Service Error",
                        detail: error.Message,
                        statusCode: StatusCodes.Status500InternalServerError
                    ),
                    _ => Problem(
                        title: "Internal Server Error", 
                        detail: "An error occurred while closing the roof",
                        statusCode: StatusCodes.Status500InternalServerError
                    )
                }
            );
        }

        /// <summary>
        /// Stops the current roof operation
        /// </summary>
        /// <returns>Updated roof controller status after stop operation</returns>
        /// <response code="200">Roof stop operation completed successfully</response>
        /// <response code="500">Internal server error or service state issue occurred</response>
        [HttpGet, Route("Stop", Name = nameof(DoRoofStop))]
        [ProducesResponseType(typeof(RoofControllerStatus), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
        public ActionResult<RoofControllerStatus> DoRoofStop()
        {
            var result = this._roofController.Stop();
            
            return result.Match(
                success: status => Ok(status),
                failure: error => error switch
                {
                    // Service state issues should return 500
                    InvalidOperationException => Problem(
                        title: "Service Error",
                        detail: error.Message,
                        statusCode: StatusCodes.Status500InternalServerError
                    ),
                    _ => Problem(
                        title: "Internal Server Error", 
                        detail: "An error occurred while stopping the roof",
                        statusCode: StatusCodes.Status500InternalServerError
                    )
                }
            );
        }

        /// <summary>
        /// Clears controller/motor fault by pulsing the clear-fault relay.
        /// </summary>
        /// <param name="pulseMs">Pulse duration in milliseconds</param>
        /// <returns>True when the pulse completed</returns>
        /// <response code="200">Fault clear pulse issued successfully</response>
        /// <response code="500">Internal server error or service state issue occurred</response>
        [HttpPost, Route("ClearFault", Name = nameof(DoClearFault))]
        [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
        public ActionResult<bool> DoClearFault([FromQuery] int pulseMs = 250)
        {
            var result = this._roofController.ClearFault(pulseMs);

            return result.Match(
                success: ok => Ok(ok),
                failure: error => error switch
                {
                    InvalidOperationException => Problem(
                        title: "Service Error",
                        detail: error.Message,
                        statusCode: StatusCodes.Status500InternalServerError
                    ),
                    _ => Problem(
                        title: "Internal Server Error",
                        detail: "An error occurred while clearing fault",
                        statusCode: StatusCodes.Status500InternalServerError
                    )
                }
            );
        }
    }
}
