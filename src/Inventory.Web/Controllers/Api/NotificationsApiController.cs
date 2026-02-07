using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Web.Controllers.Api
{
    [Route("api/notifications")]
    [ApiController]
    [Authorize]
    public class NotificationsApiController : ControllerBase
    {
        private readonly INotificationService _notificationService;

        public NotificationsApiController(INotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<PaymentNotificationDto>>> GetNotifications(CancellationToken ct)
        {
            var notifications = await _notificationService.GetActiveNotificationsAsync(ct);
            return Ok(notifications);
        }
    }
}
