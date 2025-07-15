using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using System.Device.Gpio;
using System.Device.Gpio.Drivers;
using HVO.WebSite.RoofControllerV4.Logic;
using HVO.WebSite.RoofControllerV4.Models;

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
        public ActionResult<RoofControllerStatusResponse> GetRoofStatus()
        {
            var response = new RoofControllerStatusResponse
            {
                Status = this._roofController.Status,
                IsInitialized = this._roofController.IsInitialized
            };
            return Ok(response);
        }

        [HttpGet, Route("Open", Name = nameof(DoRoofOpen))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public ActionResult<RoofControllerStatusResponse> DoRoofOpen()
        {
            var result = this._roofController.Open();
            
            return result.Match(
                success: status => 
                {
                    var response = new RoofControllerStatusResponse
                    {
                        Status = status,
                        IsInitialized = this._roofController.IsInitialized
                    };
                    return Ok(response);
                },
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

        [HttpGet, Route("Close", Name = nameof(DoRoofClose))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public ActionResult<RoofControllerStatusResponse> DoRoofClose()
        {
            var result = this._roofController.Close();
            
            return result.Match(
                success: status => 
                {
                    var response = new RoofControllerStatusResponse
                    {
                        Status = status,
                        IsInitialized = this._roofController.IsInitialized
                    };
                    return Ok(response);
                },
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

        [HttpGet, Route("Stop", Name = nameof(DoRoofStop))]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public ActionResult<RoofControllerStatusResponse> DoRoofStop()
        {
            var result = this._roofController.Stop();
            
            return result.Match(
                success: status => 
                {
                    var response = new RoofControllerStatusResponse
                    {
                        Status = status,
                        IsInitialized = this._roofController.IsInitialized
                    };
                    return Ok(response);
                },
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
    }
}
