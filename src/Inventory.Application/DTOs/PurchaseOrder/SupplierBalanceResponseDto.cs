using System;

namespace Inventory.Application.DTOs.PurchaseOrder
{
    public record SupplierBalanceResponseDto
    {
        public int SupplierId { get; init; }
        public string SupplierName { get; init; } = string.Empty;

        // ──────────────────────────────────────────────────────────────────────
        // Metric 1: Total Volume
        // Sum of ALL purchase order TotalAmounts regardless of status.
        // Represents the total business volume placed with this supplier.
        // ──────────────────────────────────────────────────────────────────────
        public decimal TotalVolume { get; init; }

        // ──────────────────────────────────────────────────────────────────────
        // Metric 2: Pending (NetOwedToSupplier)
        // Formula: sum(PO.Remaining) - sum(SSO.TotalAmount where Status != Cancelled)
        // A SupplierSalesOrder is a permanent bookkeeping entry. An active SSO
        // immediately reduces NetOwedToSupplier by its full amount.
        // ──────────────────────────────────────────────────────────────────────
        public decimal NetOwedToSupplier { get; init; }

        // ──────────────────────────────────────────────────────────────────────
        // Metric 3: Paid
        // Total cash paid OUT to this supplier from the PaymentRecord ledger
        // (PaymentType=Payment, OrderType=PurchaseOrder), net of PO refunds received.
        // SupplierSalesOrders NEVER contribute to Paid.
        // ──────────────────────────────────────────────────────────────────────
        public decimal Paid { get; init; }

        // ──────────────────────────────────────────────────────────────────────
        // Metric 4: Overdue
        // Sum of remaining balances on PurchaseOrders where DueDate < now
        // and PaymentStatus is not Paid and Status is not Cancelled.
        // SupplierSalesOrders do NOT offset the Overdue metric.
        // ──────────────────────────────────────────────────────────────────────
        public decimal Overdue { get; init; }

        public DateTimeOffset AsOfUtc { get; init; }
    }
}
