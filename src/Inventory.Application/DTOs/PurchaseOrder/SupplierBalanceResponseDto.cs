using System;

namespace Inventory.Application.DTOs.PurchaseOrder
{
    public record SupplierBalanceResponseDto
    {
        public int SupplierId { get; init; }
        public string SupplierName { get; init; } = string.Empty;

        public decimal TotalOrders { get; init; }
        public decimal TotalPayments { get; init; }

        public decimal Balance => TotalOrders - TotalPayments;

        public DateTimeOffset AsOfUtc { get; init; }
    }
}

