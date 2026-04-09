namespace Inventory.Domain.Entities;

/// <summary>
/// Represents a sale made TO a supplier (e.g. selling surplus or returned goods back to a supplier).
/// Distinct from SalesOrder which is a sale TO a customer.
/// PaymentStatus is NEVER set manually — always derived via RecalculatePaymentStatus().
/// </summary>
public class SupplierSalesOrder
{
    public long Id { get; set; }
    public string OrderNumber { get; set; } = "";

    // Linked to Supplier, NOT to Customer
    public int SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public string SupplierNameSnapshot { get; set; } = "";

    public SalesOrderStatus Status { get; set; } = SalesOrderStatus.Pending;

    /// <summary>
    /// Order creation date (local business date).
    /// </summary>
    public DateTimeOffset OrderDate { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When the supplier is expected to pay.
    /// </summary>
    public DateTimeOffset DueDate { get; set; }

    /// <summary>
    /// Payment method used for this order (cash / check / bank transfer).
    /// </summary>
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;

    /// <summary>
    /// Overall payment status. Managed exclusively by RecalculatePaymentStatus().
    /// Never assign manually.
    /// </summary>
    public PaymentStatus PaymentStatus { get; private set; } = PaymentStatus.Pending;

    /// <summary>
    /// Ledger: all payment and refund entries for this order.
    /// Must always be loaded via .Include(o => o.Payments) before calling RecalculatePaymentStatus().
    /// </summary>
    public List<PaymentRecord> Payments { get; set; } = new();

    // ─── Ledger-based status engine ────────────────────────────────────────────

    /// <summary>
    /// Derives PaymentStatus from the ledger (Payments collection).
    /// Formula: netCash = sum(payments) - sum(refunds)
    ///   netCash == 0              → Pending
    ///   0 &lt; netCash &lt; TotalAmount → PartiallyPaid
    ///   netCash >= TotalAmount    → Paid
    /// Throws if Payments collection is not loaded or net cash is negative.
    /// </summary>
    public void RecalculatePaymentStatus()
    {
        // GUARD: Payments must be loaded via .Include(o => o.Payments)
        if (Payments == null)
            throw new InvalidOperationException(
                $"Cannot recalculate payment status for SupplierSalesOrder {Id}: " +
                "Payments collection is not loaded. Ensure .Include(o => o.Payments) is used.");

        var totalPaid = Payments
            .Where(p => p.PaymentType == PaymentRecordType.Payment)
            .Sum(p => p.Amount);

        var totalRefunded = Payments
            .Where(p => p.PaymentType == PaymentRecordType.Refund)
            .Sum(p => p.Amount);

        var netCash = totalPaid - totalRefunded;

        if (netCash <= 0)
            PaymentStatus = PaymentStatus.Pending;
        else if (netCash < TotalAmount)
            PaymentStatus = PaymentStatus.PartiallyPaid;
        else
            PaymentStatus = PaymentStatus.Paid;

        // Integrity invariant: refunds must never exceed payments
        if (netCash < 0)
            throw new InvalidOperationException(
                $"Invalid state: SupplierSalesOrder {Id} has negative NetCash ({netCash:C}). " +
                "Refunded more than paid.");
    }

    // ─── Ledger helper methods ──────────────────────────────────────────────────

    public decimal GetTotalPaid()
    {
        if (Payments == null) return 0;
        return Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount);
    }

    public decimal GetTotalRefunded()
    {
        if (Payments == null) return 0;
        return Payments.Where(p => p.PaymentType == PaymentRecordType.Refund).Sum(p => p.Amount);
    }

    /// <summary>
    /// Net money received (payments minus refunds).
    /// </summary>
    public decimal GetNetCash() => GetTotalPaid() - GetTotalRefunded();

    /// <summary>
    /// Amount still owed by the supplier.
    /// </summary>
    public decimal GetPendingAmount() => Math.Max(0, TotalAmount - GetNetCash());

    /// <summary>
    /// Amount that can still be refunded (money we hold from this order).
    /// </summary>
    public decimal GetRefundableAmount() => GetNetCash();

    // ─── Invariant guards ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the refund is allowable based on the ledger.
    /// Requirement: refundable > 0 regardless of PaymentStatus.
    /// Blocks if refundAmount > refundable.
    /// </summary>
    public bool CanRefund(decimal refundAmount, out string error)
    {
        var refundable = GetRefundableAmount();
        if (refundable <= 0)
        {
            error = $"SupplierSalesOrder {Id} has no refundable amount. NetCash = {GetNetCash():C}.";
            return false;
        }
        if (refundAmount > refundable)
        {
            error = $"Refund amount {refundAmount:C} exceeds refundable amount {refundable:C} on SupplierSalesOrder {Id}.";
            return false;
        }
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Returns true if the order can be cancelled.
    /// Blocks if NetPaid > 0 (money still held) or stock not fully reversed.
    /// </summary>
    public bool CanCancel(out string error)
    {
        var netCash = GetNetCash();
        if (netCash != 0)
        {
            error = $"SupplierSalesOrder {Id} cannot be cancelled while financial imbalance exists. " +
                    $"Net Cash: {netCash:C}. All payments must be fully refunded first.";
            return false;
        }

        if (Status == SalesOrderStatus.Done)
        {
            var remainingStock = Lines.Sum(l => l.Quantity - l.RefundedQuantity);
            if (remainingStock > 0)
            {
                error = $"SupplierSalesOrder {Id} cannot be cancelled while stock movement exists. " +
                        $"Remaining stock to reverse: {remainingStock}. Process a full stock return first.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    // ─── Check / transfer metadata ──────────────────────────────────────────────

    public bool? CheckReceived { get; set; }
    public DateTimeOffset? CheckReceivedDate { get; set; }
    public bool? CheckCashed { get; set; }
    public DateTimeOffset? CheckCashedDate { get; set; }
    public string? TransferId { get; set; }

    // ─── Document attachments ───────────────────────────────────────────────────

    public string? InvoicePath { get; set; }
    public DateTimeOffset? InvoiceUploadedUtc { get; set; }
    public string? ReceiptPath { get; set; }
    public DateTimeOffset? ReceiptUploadedUtc { get; set; }

    // ─── Audit ──────────────────────────────────────────────────────────────────

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedByUserId { get; set; } = "";
    public string CreatedByUserDisplayName { get; set; } = "";
    public string? Note { get; set; }

    // ─── Tax fields ─────────────────────────────────────────────────────────────

    public bool IsTaxInclusive { get; set; } = false;
    public bool ApplyVat { get; set; } = true;
    public bool ApplyManufacturingTax { get; set; } = true;
    public decimal Subtotal { get; set; }
    public decimal VatAmount { get; set; }
    public decimal ManufacturingTaxAmount { get; set; }
    public decimal TotalAmount { get; set; }

    /// <summary>
    /// Cumulative monetary refunds issued against this order.
    /// Must never exceed TotalAmount.
    /// </summary>
    public decimal RefundedAmount { get; set; }

    public List<SupplierSalesOrderLine> Lines { get; set; } = new();
}

public class SupplierSalesOrderLine
{
    public long Id { get; set; }
    public long SupplierSalesOrderId { get; set; }
    public SupplierSalesOrder? SupplierSalesOrder { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public string ProductNameSnapshot { get; set; } = "";

    /// <summary>
    /// Optional batch/lot number for this line.
    /// </summary>
    public string? BatchNumber { get; set; }

    public long? ProductBatchId { get; set; }
    public ProductBatch? ProductBatch { get; set; }

    public decimal Quantity { get; set; }
    public string UnitSnapshot { get; set; } = "";

    /// <summary>
    /// Price per unit at time of order (snapshot).
    /// </summary>
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// Total quantity of this line that has been refunded (stock returned).
    /// Must never exceed Quantity.
    /// </summary>
    public decimal RefundedQuantity { get; set; }

    // Tax fields
    public decimal LineSubtotal { get; set; }
    public decimal LineVatAmount { get; set; }
    public decimal LineManufacturingTaxAmount { get; set; }
    public decimal LineTotal { get; set; }
}
