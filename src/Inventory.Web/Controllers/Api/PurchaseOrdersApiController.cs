using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.PurchaseOrder;
using Inventory.Application.DTOs.PurchaseOrder;
using Inventory.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Web.Controllers.Api;

[Route("api/purchase-orders")]
[ApiController]
public class PurchaseOrdersApiController : ControllerBase
{
    private readonly IPurchaseOrderServices _purchaseOrderServices;

    public PurchaseOrdersApiController(IPurchaseOrderServices purchaseOrderServices)
    {
        _purchaseOrderServices = purchaseOrderServices;
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<PurchaseOrderResponse>> GetById(int id, CancellationToken ct)
    {
        var order = await _purchaseOrderServices.GetByIdAsync(id, ct);
        if (order is null)
        {
            return NotFound();
        }
        return Ok(order);
    }

    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateStatus(long id, [FromBody] PurchaseOrderStatus status, CancellationToken ct)
    {
        var user = GetUserContext();
        await _purchaseOrderServices.UpdateStatusAsync(id, status, user, ct);
        return Ok();
    }

    [HttpPut("{id}/payment-status")]
    public async Task<IActionResult> UpdatePaymentStatus(long id, [FromBody] PurchasePaymentStatus status, CancellationToken ct)
    {
        var user = GetUserContext();
        await _purchaseOrderServices.UpdatePaymentStatusAsync(id, status, user, ct);
        return Ok();
    }

    [HttpPost("{id}/refund")]
    public async Task<IActionResult> Refund(long id, [FromBody] RefundPurchaseOrderRequest request, CancellationToken ct)
    {
        if (id != request.OrderId) return BadRequest();
        var user = GetUserContext();
        await _purchaseOrderServices.RefundAsync(request, user, ct);
        return Ok();
    }


    [HttpPost("{id}/invoice")]
    public async Task<IActionResult> UploadInvoice(long id, IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file uploaded.");

        if (file.ContentType != "application/pdf")
            return BadRequest("Only PDF files are allowed.");

        // Ensure directory exists
        var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "purchase-orders", "invoices");
        if (!Directory.Exists(uploadsFolder))
            Directory.CreateDirectory(uploadsFolder);

        // Generate filename: PO-{id}-INV-{Guid}.pdf
        var fileName = $"PO-{id}-INV-{Guid.NewGuid()}.pdf";
        var filePath = Path.Combine(uploadsFolder, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream, ct);
        }

        // Relative path for storage (e.g. /uploads/purchase-orders/invoices/...)
        var relativePath = $"/uploads/purchase-orders/invoices/{fileName}";
        var user = GetUserContext();

        try
        {
            await _purchaseOrderServices.AttachInvoiceAsync(id, relativePath, user, ct);
        }
        catch (Exception)
        {
            // Cleanup file if DB update fails
            if (System.IO.File.Exists(filePath))
                System.IO.File.Delete(filePath);
            throw;
        }

        return Ok(new { path = relativePath });
    }

    [HttpDelete("{id}/invoice")]
    public async Task<IActionResult> DeleteInvoice(long id, CancellationToken ct)
    {
        var user = GetUserContext();
        
        // Get order to find file path to delete
        var order = await _purchaseOrderServices.GetByIdAsync(id, ct);
        if (order == null) return NotFound();

        if (!string.IsNullOrEmpty(order.InvoicePath))
        {
            // Convert relative web path to absolute system path
            var relativePath = order.InvoicePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", relativePath);
            
            if (System.IO.File.Exists(fullPath))
            {
                System.IO.File.Delete(fullPath);
            }
        }

        await _purchaseOrderServices.RemoveInvoiceAsync(id, user, ct);

        return NoContent();
    }

    [HttpPost("{id}/receipt")]
    public async Task<IActionResult> UploadReceipt(long id, IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file uploaded.");

        if (file.ContentType != "application/pdf")
            return BadRequest("Only PDF files are allowed.");

        // Ensure directory exists
        var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "purchase-orders", "receipts");
        if (!Directory.Exists(uploadsFolder))
            Directory.CreateDirectory(uploadsFolder);

        // Generate filename: PO-{id}-RCPT-{Guid}.pdf
        var fileName = $"PO-{id}-RCPT-{Guid.NewGuid()}.pdf";
        var filePath = Path.Combine(uploadsFolder, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream, ct);
        }

        // Relative path for storage
        var relativePath = $"/uploads/purchase-orders/receipts/{fileName}";
        var user = GetUserContext();

        try
        {
            await _purchaseOrderServices.AttachReceiptAsync(id, relativePath, user, ct);
        }
        catch (Exception)
        {
            // Cleanup file if DB update fails
            if (System.IO.File.Exists(filePath))
                System.IO.File.Delete(filePath);
            throw;
        }

        return Ok(new { path = relativePath });
    }

    [HttpDelete("{id}/receipt")]
    public async Task<IActionResult> DeleteReceipt(long id, CancellationToken ct)
    {
        var user = GetUserContext();

        // Get order to find file path to delete
        var order = await _purchaseOrderServices.GetByIdAsync(id, ct);
        if (order == null) return NotFound();

        if (!string.IsNullOrEmpty(order.ReceiptPath))
        {
            // Convert relative web path to absolute system path
            var relativePath = order.ReceiptPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", relativePath);

            if (System.IO.File.Exists(fullPath))
            {
                System.IO.File.Delete(fullPath);
            }
        }

        await _purchaseOrderServices.RemoveReceiptAsync(id, user, ct);

        return NoContent();
    }


    private UserContext GetUserContext()
    {
        var userId = User?.Identity?.Name ?? "system";
        var displayName = User?.Identity?.Name ?? "System";
        return new UserContext(userId, displayName);
    }
}
