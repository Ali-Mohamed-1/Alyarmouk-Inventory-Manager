using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.Customer;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Web.Controllers.Api;

[ApiController]
[Route("api/customers")]
public sealed class CustomersApiController : ControllerBase
{
    private readonly ICustomerServices _customers;
    private readonly ISalesOrderServices _salesOrders;
    private readonly IReportingServices _reporting;

    public CustomersApiController(
        ICustomerServices customers,
        ISalesOrderServices salesOrders,
        IReportingServices reporting)
    {
        _customers = customers;
        _salesOrders = salesOrders;
        _reporting = reporting;
    }

    /// <summary>
    /// Get all customers with basic information
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var customers = await _customers.GetAllAsync(cancellationToken);
        return Ok(customers);
    }

    /// <summary>
    /// Get customer details including balance information
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var customer = await _customers.GetByIdAsync(id, cancellationToken);
        if (customer is null)
        {
            return NotFound(new { message = $"Customer with ID {id} not found." });
        }

        var balance = await _reporting.GetCustomerBalanceAsync(id, DateTimeOffset.UtcNow, cancellationToken);

        return Ok(new
        {
            customer,
            balance
        });
    }

    /// <summary>
    /// Get all sales orders for a specific customer
    /// </summary>
    [HttpGet("{id:int}/sales-orders")]
    public async Task<IActionResult> GetSalesOrders(int id, CancellationToken cancellationToken)
    {
        var orders = await _salesOrders.GetCustomerOrdersAsync(id, take: 1000, cancellationToken);
        return Ok(orders);
    }

    /// <summary>
    /// Create a new customer
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var user = GetUserContext();
        var id = await _customers.CreateAsync(request, user, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id }, new { id });
    }

    /// <summary>
    /// Update customer information
    /// </summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCustomerRequest request, CancellationToken cancellationToken)
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
        await _customers.UpdateAsync(id, request, user, cancellationToken);

        return NoContent();
    }

    private UserContext GetUserContext()
    {
        var userId = User?.Identity?.Name ?? "system";
        var displayName = User?.Identity?.Name ?? "System";
        return new UserContext(userId, displayName);
    }
}
