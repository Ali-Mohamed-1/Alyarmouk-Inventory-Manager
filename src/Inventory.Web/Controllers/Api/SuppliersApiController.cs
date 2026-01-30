using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.Supplier;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Web.Controllers.Api;

[ApiController]
[Route("api/suppliers")]
public sealed class SuppliersApiController : ControllerBase
{
    private readonly ISupplierServices _suppliers;
    private readonly IPurchaseOrderServices _purchaseOrders;
    private readonly IReportingServices _reporting;

    public SuppliersApiController(
        ISupplierServices suppliers,
        IPurchaseOrderServices purchaseOrders,
        IReportingServices reporting)
    {
        _suppliers = suppliers;
        _purchaseOrders = purchaseOrders;
        _reporting = reporting;
    }

    /// <summary>
    /// Get all suppliers with basic information
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var suppliers = await _suppliers.GetAllAsync(cancellationToken);
        return Ok(suppliers);
    }

    /// <summary>
    /// Get supplier details including balance information
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var supplier = await _suppliers.GetByIdAsync(id, cancellationToken);
        if (supplier is null)
        {
            return NotFound(new { message = $"Supplier with ID {id} not found." });
        }

        var balance = await _reporting.GetSupplierBalanceAsync(id, cancellationToken);

        return Ok(new
        {
            supplier,
            balance
        });
    }

    /// <summary>
    /// Get all purchase orders for a specific supplier
    /// </summary>
    [HttpGet("{id:int}/purchase-orders")]
    public async Task<IActionResult> GetPurchaseOrders(int id, CancellationToken cancellationToken)
    {
        var orders = await _purchaseOrders.GetBySupplierAsync(id, cancellationToken);
        return Ok(orders);
    }

    /// <summary>
    /// Create a new supplier
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSupplierRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var user = GetUserContext();
        var id = await _suppliers.CreateAsync(request, user, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id }, new { id });
    }

    /// <summary>
    /// Update supplier information
    /// </summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateSupplierRequest request, CancellationToken cancellationToken)
    {
        if (id != request.Id)
        {
            return BadRequest(new { message = "ID mismatch between route and request body." });
        }

        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var user = GetUserContext();
        await _suppliers.UpdateAsync(id, request, user, cancellationToken);

        return NoContent();
    }

    private UserContext GetUserContext()
    {
        var userId = User?.Identity?.Name ?? "system";
        var displayName = User?.Identity?.Name ?? "System";
        return new UserContext(userId, displayName);
    }
}
