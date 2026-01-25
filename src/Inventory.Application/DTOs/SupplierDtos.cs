using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Inventory.Application.DTOs
{
    public record CreateSupplierRequest(
        string Name,
        string? Phone,
        string? Email,
        string? Address);

    public record UpdateSupplierRequest(
        int Id,
        string Name,
        string? Phone,
        string? Email,
        string? Address,
        bool IsActive);

    public record SupplierResponse(
        int Id,
        string Name,
        string? Phone,
        string? Email,
        string? Address,
        bool IsActive,
        DateTimeOffset CreatedUtc);
}
