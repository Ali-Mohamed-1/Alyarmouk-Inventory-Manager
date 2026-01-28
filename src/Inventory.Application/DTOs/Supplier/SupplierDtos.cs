using System;

namespace Inventory.Application.DTOs.Supplier
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

    public record SupplierDropdownResponse(
        int Id,
        string Name);
}
