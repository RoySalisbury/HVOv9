using Microsoft.AspNetCore.Mvc;

namespace HVO.WebSite.Playground.Controllers;

/// <summary>
/// Home controller for handling web page requests
/// </summary>
public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    /// <summary>
    /// Initializes a new instance of the HomeController
    /// </summary>
    /// <param name="logger">Logger for tracking home controller operations</param>
    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Displays the home page
    /// </summary>
    /// <returns>The home page view</returns>
    public IActionResult Index()
    {
        return View();
    }

    
    /// <summary>
    /// Displays the Health Check MVC test page
    /// </summary>
    /// <returns>The Health Check MVC view</returns>
    public IActionResult HealthCheckMVC()
    {
        return View("HealthCheckMVC");
    }
}
