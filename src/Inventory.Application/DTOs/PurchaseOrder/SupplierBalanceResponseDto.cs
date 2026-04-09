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
        // Formula: sum(PO.Remaining) - sum(SSO.Remaining where Status != Cancelled)
        // A SupplierSalesOrder reduces what we owe because the supplier owes US.
        // ──────────────────────────────────────────────────────────────────────
        public decimal NetOwedToSupplier { get; init; }

        // ──────────────────────────────────────────────────────────────────────
        // Metric 3: Paid
        // Total cash paid OUT to this supplier from the PaymentRecord ledger
        // (PaymentType=Payment, OrderType=PurchaseOrder), net of PO refunds received.
        // ──────────────────────────────────────────────────────────────────────
        public decimal Paid { get; init; }

        // ──────────────────────────────────────────────────────────────────────
        // Metric 4: Overdue
        // Remaining balance on PurchaseOrders where PaymentDeadline < now
        // and PaymentStatus is not Paid and Status is not Cancelled,
        // reduced by active (non-cancelled) SupplierSalesOrder remaining amounts.
        // Clamped to 0 minimum.
        // ──────────────────────────────────────────────────────────────────────
        public decimal Overdue { get; init; }

        public DateTimeOffset AsOfUtc { get; init; }
    }
}
