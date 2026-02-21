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
    private readonly IWebHostEnvironment _env;

    public SalesOrdersApiController(ISalesOrderServices salesOrderServices, IWebHostEnvironment env)
    {
        _salesOrderServices = salesOrderServices;
        _env = env;
    }

    private UserContext GetUserContext()
    {
        var userId = User?.Identity?.Name ?? "system";
        var displayName = User?.Identity?.Name ?? "System";
        return new UserContext(userId, displayName);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<SalesOrderResponseDto>> GetById(long id, CancellationToken ct)
    {
        var order = await _salesOrderServices.GetByIdAsync(id, ct);
        if (order is null)
        {
            return NotFound();
        }
        return Ok(order);
    }

    [HttpPost("{id}/invoice")]
    public async Task<IActionResult> UploadInvoice(long id, IFormFile file, CancellationToken ct)
    {
        var validationError = ValidateFile(file);
        if (validationError != null) return BadRequest(validationError);

        // Ensure directory exists
        var uploadPath = Path.Combine(_env.WebRootPath, "uploads", "orders", "invoices");
        if (!Directory.Exists(uploadPath))
            Directory.CreateDirectory(uploadPath);

        // Secure filename: {Guid}_{SanitizedName}
        var sanitizedName = Path.GetFileName(file.FileName).Replace(" ", "_");
        var fileName = $"{Guid.NewGuid()}_{sanitizedName}";
        var filePath = Path.Combine(uploadPath, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream, ct);
        }

        // Relative path for storage
        var relativePath = $"/uploads/orders/invoices/{fileName}";
        var user = GetUserContext();

        try
        {
            await _salesOrderServices.AttachInvoiceAsync(id, relativePath, user, ct);
        }
        catch (Exception)
        {
            if (System.IO.File.Exists(filePath)) System.IO.File.Delete(filePath);
            throw;
        }

        return Ok(new { path = relativePath });
    }

    [HttpDelete("{id}/invoice")]
    public async Task<IActionResult> DeleteInvoice(long id, CancellationToken ct)
    {
        var user = GetUserContext();
        
        // Get order to find file path
        var order = await _salesOrderServices.GetByIdAsync(id, ct);
        if (order == null) return NotFound();

        if (!string.IsNullOrEmpty(order.InvoicePath))
        {
            var relativePath = order.InvoicePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.Combine(_env.WebRootPath, relativePath);
            if (System.IO.File.Exists(fullPath))
            {
                System.IO.File.Delete(fullPath);
            }
        }

        await _salesOrderServices.RemoveInvoiceAsync(id, user, ct);

        return NoContent();
    }

    [HttpPost("{id}/receipt")]
    public async Task<IActionResult> UploadReceipt(long id, IFormFile file, CancellationToken ct)
    {
        var validationError = ValidateFile(file);
        if (validationError != null) return BadRequest(validationError);

        // Ensure directory exists
        var uploadPath = Path.Combine(_env.WebRootPath, "uploads", "orders", "receipts");
        if (!Directory.Exists(uploadPath))
            Directory.CreateDirectory(uploadPath);

        // Secure filename: {Guid}_{SanitizedName}
        var sanitizedName = Path.GetFileName(file.FileName).Replace(" ", "_");
        var fileName = $"{Guid.NewGuid()}_{sanitizedName}";
        var filePath = Path.Combine(uploadPath, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream, ct);
        }

        // Relative path for storage
        var relativePath = $"/uploads/orders/receipts/{fileName}";
        var user = GetUserContext();

        try
        {
            await _salesOrderServices.AttachReceiptAsync(id, relativePath, user, ct);
        }
        catch (Exception)
        {
            if (System.IO.File.Exists(filePath)) System.IO.File.Delete(filePath);
            throw;
        }

        return Ok(new { path = relativePath });
    }

    [HttpDelete("{id}/receipt")]
    public async Task<IActionResult> DeleteReceipt(long id, CancellationToken ct)
    {
        var user = GetUserContext();
        
        // Get order to find file path
        var order = await _salesOrderServices.GetByIdAsync(id, ct);
        if (order == null) return NotFound();

        if (!string.IsNullOrEmpty(order.ReceiptPath))
        {
            var relativePath = order.ReceiptPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.Combine(_env.WebRootPath, relativePath);
            if (System.IO.File.Exists(fullPath))
            {
                System.IO.File.Delete(fullPath);
            }
        }

        await _salesOrderServices.RemoveReceiptAsync(id, user, ct);

        return NoContent();
    }

    [HttpPut("{id}/payment")]
    public async Task<IActionResult> UpdatePaymentInfo(long id, [FromBody] UpdateSalesOrderPaymentRequest request, CancellationToken ct)
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

    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> Cancel(long id, CancellationToken ct)
    {
        var user = GetUserContext();
        await _salesOrderServices.CancelAsync(id, user, ct);
        return Ok();
    }

    [HttpPost("{id}/payments")]
    public async Task<IActionResult> AddPayment(long id, [FromBody] Inventory.Application.DTOs.Payment.CreatePaymentRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var user = GetUserContext();
        await _salesOrderServices.AddPaymentAsync(id, request, user, ct);
        
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
}
