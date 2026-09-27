using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using UAI.Models;

namespace UAI.Controllers;

public sealed class HomeController : Controller
{
    private readonly ILogger<HomeController> _log;

    public HomeController(ILogger<HomeController> log) => _log = log;

    public IActionResult Index() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
