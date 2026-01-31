using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Web.Controllers;

[Authorize]
public class ActivityController : Controller
{
    public IActionResult Index()
    {
        return View();
    }
}
