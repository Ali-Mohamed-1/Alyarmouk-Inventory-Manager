using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.SalesOrder;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Web.Controllers.Api;

[Route("api/sales-orders")]
[ApiController]
public class SalesOrdersApiController : ControllerBase
{
    private readonly ISalesOrderServices _salesOrderServices;

    public SalesOrdersApiController(ISalesOrderServices salesOrderServices)
    {
        _salesOrderServices = salesOrderServices;
    }

    private UserContext GetUserContext()
    {
        var userId = User?.Identity?.Name ?? "system";
        var displayName = User?.Identity?.Name ?? "System";
        return new UserContext(userId, displayName);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<SalesOrderResponseDto>> GetById(int id, CancellationToken ct)
    {
        var order = await _salesOrderServices.GetByIdAsync(id, ct);
        if (order is null)
        {
            return NotFound();
        }
        return Ok(order);
    }

    [HttpPost("{id}/invoice")]
    public async Task<IActionResult> UploadInvoice(int id, IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file uploaded.");

        if (!file.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Only PDF files are allowed.");

        // Ensure directory exists
        var uploadPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "orders", "invoices");
        if (!Directory.Exists(uploadPath))
            Directory.CreateDirectory(uploadPath);

        // Generate unique filename
        var fileName = $"INV-{id}-{Guid.NewGuid()}.pdf";
        var filePath = Path.Combine(uploadPath, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream, ct);
        }

        // Relative path for storage
        var relativePath = $"/uploads/orders/invoices/{fileName}";
        var user = GetUserContext();

        await _salesOrderServices.AttachInvoiceAsync(id, relativePath, user, ct);

        return Ok(new { path = relativePath });
    }

    [HttpDelete("{id}/invoice")]
    public async Task<IActionResult> DeleteInvoice(int id, CancellationToken ct)
    {
        var user = GetUserContext();
        
        // Get order to find file path
        var order = await _salesOrderServices.GetByIdAsync(id, ct);
        if (order == null) return NotFound();

        if (!string.IsNullOrEmpty(order.InvoicePath))
        {
            var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", order.InvoicePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(fullPath))
            {
                System.IO.File.Delete(fullPath);
            }
        }

        await _salesOrderServices.RemoveInvoiceAsync(id, user, ct);

        return NoContent();
    }

    [HttpPost("{id}/receipt")]
    public async Task<IActionResult> UploadReceipt(int id, IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file uploaded.");

        if (!file.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Only PDF files are allowed.");

        // Ensure directory exists
        var uploadPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "orders", "receipts");
        if (!Directory.Exists(uploadPath))
            Directory.CreateDirectory(uploadPath);

        // Generate unique filename
        var fileName = $"REC-{id}-{Guid.NewGuid()}.pdf";
        var filePath = Path.Combine(uploadPath, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream, ct);
        }

        // Relative path for storage
        var relativePath = $"/uploads/orders/receipts/{fileName}";
        var user = GetUserContext();

        await _salesOrderServices.AttachReceiptAsync(id, relativePath, user, ct);

        return Ok(new { path = relativePath });
    }

    [HttpDelete("{id}/receipt")]
    public async Task<IActionResult> DeleteReceipt(int id, CancellationToken ct)
    {
        var user = GetUserContext();
        
        // Get order to find file path
        var order = await _salesOrderServices.GetByIdAsync(id, ct);
        if (order == null) return NotFound();

        if (!string.IsNullOrEmpty(order.ReceiptPath))
        {
            var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", order.ReceiptPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(fullPath))
            {
                System.IO.File.Delete(fullPath);
            }
        }

        await _salesOrderServices.RemoveReceiptAsync(id, user, ct);

        return NoContent();
    }

    [HttpPut("{id}/payment")]
    public async Task<IActionResult> UpdatePaymentInfo(int id, [FromBody] UpdateSalesOrderPaymentRequest request, CancellationToken ct)
    {
        if (id != request.OrderId)
            return BadRequest("Order ID mismatch.");

        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var user = GetUserContext();
        await _salesOrderServices.UpdatePaymentInfoAsync(id, request, user, ct);
        
        return NoContent();
    }

    [HttpPut("{id}/due-date")]
    public async Task<IActionResult> UpdateDueDate(long id, [FromBody] DateTimeOffset newDate, CancellationToken ct)
    {
        var user = GetUserContext();
        await _salesOrderServices.UpdateDueDateAsync(id, newDate, user, ct);
        return Ok();
    }

    [HttpPost("{id}/refund")]
    public async Task<IActionResult> Refund(long id, [FromBody] RefundSalesOrderRequest request, CancellationToken ct)
    {
        if (id != request.OrderId) return BadRequest("Order ID mismatch.");
        
        var user = GetUserContext();
        await _salesOrderServices.RefundAsync(request, user, ct);
        
        return Ok();
    }
}
