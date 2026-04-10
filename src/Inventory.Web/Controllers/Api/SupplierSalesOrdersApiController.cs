using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.SupplierSalesOrder;
using Inventory.Application.DTOs.Payment;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Web.Controllers.Api;

[Route("api/supplier-sales-orders")]
[ApiController]
public class SupplierSalesOrdersApiController : ControllerBase
{
    private readonly ISupplierSalesOrderServices _services;

    public SupplierSalesOrdersApiController(ISupplierSalesOrderServices services)
    {
        _services = services;
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<SupplierSalesOrderResponseDto>> GetById(long id, CancellationToken ct)
    {
        var order = await _services.GetByIdAsync(id, ct);
        if (order is null) return NotFound();
        return Ok(order);
    }

    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> Cancel(long id, CancellationToken ct)
    {
        var user = GetUserContext();
        await _services.CancelAsync(id, user, ct);
        return Ok();
    }

    [HttpPost("{id}/refund")]
    public async Task<IActionResult> Refund(long id, [FromBody] RefundSupplierSalesOrderRequest request, CancellationToken ct)
    {
        if (id != request.SupplierSalesOrderId) return BadRequest("Order ID mismatch.");
        
        var user = GetUserContext();
        await _services.RefundAsync(request, user, ct);
        return Ok();
    }

    [HttpPost("{id}/payments")]
    public async Task<IActionResult> AddPayment(long id, [FromBody] CreatePaymentRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var user = GetUserContext();
        await _services.AddPaymentAsync(id, request, user, ct);
        return Ok();
    }

    private UserContext GetUserContext()
    {
        var userId = User?.Identity?.Name ?? "system";
        var displayName = User?.Identity?.Name ?? "System";
        return new UserContext(userId, displayName);
    }
}
