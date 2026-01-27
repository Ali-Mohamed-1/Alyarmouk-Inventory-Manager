using System;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Web.Controllers;

public sealed class AuditLogsController : Controller
{
    private readonly IAuditLogServices _auditLogs;

    public AuditLogsController(IAuditLogServices auditLogs)
    {
        _auditLogs = auditLogs;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int take = 50, CancellationToken cancellationToken = default)
    {
        var items = await _auditLogs.GetRecentAsync(take, cancellationToken);
        return View(items);
    }

    [HttpGet]
    public async Task<IActionResult> ByEntityType(string entityType, CancellationToken cancellationToken)
    {
        var items = await _auditLogs.GetByEntityTypeAsync(entityType, cancellationToken);
        ViewBag.EntityType = entityType;
        return View(items);
    }

    [HttpGet]
    public async Task<IActionResult> ByEntity(string entityType, string entityId, CancellationToken cancellationToken)
    {
        var items = await _auditLogs.GetByEntityAsync(entityType, entityId, cancellationToken);
        ViewBag.EntityType = entityType;
        ViewBag.EntityId = entityId;
        return View(items);
    }

    [HttpGet]
    public async Task<IActionResult> ByUser(string userId, CancellationToken cancellationToken)
    {
        var items = await _auditLogs.GetByUserAsync(userId, cancellationToken);
        ViewBag.UserId = userId;
        return View(items);
    }

    [HttpGet]
    public async Task<IActionResult> ByDateRange(DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken)
    {
        var items = await _auditLogs.GetByDateRangeAsync(start, end, cancellationToken);
        ViewBag.Start = start;
        ViewBag.End = end;
        return View(items);
    }
}

