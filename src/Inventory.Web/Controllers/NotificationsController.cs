using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Web.Controllers
{
    [Authorize]
    public class NotificationsController : Controller
    {
        [Route("Notifications")]
        public IActionResult Index()
        {
            return View();
        }
    }
}
