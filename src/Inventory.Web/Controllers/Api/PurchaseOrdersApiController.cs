using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.PurchaseOrder;
using Inventory.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Web.Controllers.Api;

[Route("api/purchase-orders")]
[ApiController]
public class PurchaseOrdersApiController : ControllerBase
{
    private readonly IPurchaseOrderServices _purchaseOrderServices;
    private readonly IWebHostEnvironment _env;

    public PurchaseOrdersApiController(IPurchaseOrderServices purchaseOrderServices, IWebHostEnvironment env)
    {
        _purchaseOrderServices = purchaseOrderServices;
        _env = env;
    }

    [HttpPost]
    public async Task<ActionResult<long>> Create([FromBody] CreatePurchaseOrderRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var user = GetUserContext();
        var id = await _purchaseOrderServices.CreateAsync(request, user, ct);
        return Ok(id);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<PurchaseOrderResponse>> GetById(long id, CancellationToken ct)
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
        await _purchaseOrderServices.UpdateStatusAsync(id, status, user, ct: ct);
        return Ok();
    }

    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> Cancel(long id, CancellationToken ct)
    {
        var user = GetUserContext();
        await _purchaseOrderServices.CancelAsync(id, user, ct);
        return Ok();
    }

    [HttpPut("{id}/payment-deadline")]
    public async Task<IActionResult> UpdatePaymentDeadline(long id, [FromBody] DateTimeOffset? newDeadline, CancellationToken ct)
    {
        var user = GetUserContext();
        await _purchaseOrderServices.UpdatePaymentDeadlineAsync(id, newDeadline, user, ct);
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
        var validationError = ValidateFile(file);
        if (validationError != null) return BadRequest(validationError);

        // Ensure directory exists
        var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "purchase-orders", "invoices");
        if (!Directory.Exists(uploadsFolder))
            Directory.CreateDirectory(uploadsFolder);

        // Secure filename: {Guid}_{SanitizedName}
        var sanitizedName = Path.GetFileName(file.FileName).Replace(" ", "_");
        var fileName = $"{Guid.NewGuid()}_{sanitizedName}";
        var filePath = Path.Combine(uploadsFolder, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream, ct);
        }

        // Relative path for storage
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
            var fullPath = Path.Combine(_env.WebRootPath, relativePath);
            
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
        var validationError = ValidateFile(file);
        if (validationError != null) return BadRequest(validationError);

        // Ensure directory exists
        var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "purchase-orders", "receipts");
        if (!Directory.Exists(uploadsFolder))
            Directory.CreateDirectory(uploadsFolder);

        // Secure filename: {Guid}_{SanitizedName}
        var sanitizedName = Path.GetFileName(file.FileName).Replace(" ", "_");
        var fileName = $"{Guid.NewGuid()}_{sanitizedName}";
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
            var fullPath = Path.Combine(_env.WebRootPath, relativePath);

            if (System.IO.File.Exists(fullPath))
            {
                System.IO.File.Delete(fullPath);
            }
        }

        await _purchaseOrderServices.RemoveReceiptAsync(id, user, ct);

        return NoContent();
    }


    [HttpPost("{id}/payments")]
    public async Task<IActionResult> AddPayment(long id, [FromBody] Inventory.Application.DTOs.Payment.CreatePaymentRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var user = GetUserContext();
        await _purchaseOrderServices.AddPaymentAsync(id, request, user, ct);
        
        return Ok();
    }

    [HttpPut("{id}/payment-info")]
    public async Task<IActionResult> UpdatePaymentInfo(long id, [FromBody] UpdatePurchaseOrderPaymentRequest request, CancellationToken ct)
    {
        if (id != request.OrderId) return BadRequest();
        var user = GetUserContext();
        await _purchaseOrderServices.UpdatePaymentInfoAsync(id, request, user, ct);
        return Ok();
    }

    private string? ValidateFile(IFormFile? file)
    {
        if (file == null || file.Length == 0)
            return "No file uploaded.";

        if (file.Length > 10 * 1024 * 1024)
            return "File size exceeds 10MB limit.";

        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".pdf", ".doc", ".docx" };
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowedExtensions.Contains(extension))
            return "Invalid file type. Allowed: JPG, PNG, WEBP, PDF, DOC, DOCX.";

        var allowedMimeTypes = new[] 
        { 
            "image/jpeg", "image/png", "image/webp", 
            "application/pdf", 
            "application/msword", 
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" 
        };
        if (!allowedMimeTypes.Contains(file.ContentType))
            return "Invalid MIME type.";

        return null;
    }

    private UserContext GetUserContext()
    {
        var userId = User?.Identity?.Name ?? "system";
        var displayName = User?.Identity?.Name ?? "System";
        return new UserContext(userId, displayName);
    }
}
