using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Inventory.Application.DTOs
{


    // --- RESPONSES ---

    public record PurchaseOrderResponse(
        long Id,
        string OrderNumber,
        int SupplierId,
        string SupplierName,
        DateTimeOffset CreatedUtc,
        string CreatedByUserDisplayName,
        bool IsTaxInclusive,
        decimal Subtotal,
        decimal VatAmount,
        decimal ManufacturingTaxAmount,
        decimal ReceiptExpenses,
        decimal TotalAmount,
        string? Note,
        List<PurchaseOrderLineResponse> Lines);

    public record PurchaseOrderLineResponse(
        long Id,
        int ProductId,
        string ProductName,
        decimal Quantity,
        string Unit,
        decimal UnitPrice,
        decimal LineSubtotal,
        decimal LineVatAmount,
        decimal LineTotal);
}
