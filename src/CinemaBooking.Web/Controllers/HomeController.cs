using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using CinemaBooking.Web.Models;

namespace CinemaBooking.Web.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    /// <param name="traceId">Do ExceptionHandlingMiddleware truyền sang khi chạy lại pipeline.</param>
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error(string? traceId)
    {
        return View(new ErrorViewModel
        {
            RequestId = traceId ?? Activity.Current?.Id ?? HttpContext.TraceIdentifier,
        });
    }
}
