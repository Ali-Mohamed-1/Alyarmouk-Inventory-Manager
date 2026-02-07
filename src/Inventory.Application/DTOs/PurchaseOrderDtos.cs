using Inventory.Domain.Entities;
using Inventory.Application.DTOs.Payment;
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
        DateTimeOffset? PaymentDeadline,
        Inventory.Domain.Entities.PurchaseOrderStatus Status,
        Inventory.Domain.Entities.PurchasePaymentStatus PaymentStatus,
        string CreatedByUserDisplayName,
        bool IsTaxInclusive,
        bool ApplyVat,
        bool ApplyManufacturingTax,
        decimal Subtotal,
        decimal VatAmount,
        decimal ManufacturingTaxAmount,
        decimal ReceiptExpenses,
        decimal TotalAmount,
        decimal RefundedAmount,
        string? Note,
        string? InvoicePath,
        DateTimeOffset? InvoiceUploadedUtc,
        string? ReceiptPath,
        DateTimeOffset? ReceiptUploadedUtc,
        PaymentMethod PaymentMethod,
        bool? CheckReceived,
        DateTimeOffset? CheckReceivedDate,
        bool? CheckCashed,
        DateTimeOffset? CheckCashedDate,
        string? TransferId,
        decimal PaidAmount,
        decimal RemainingAmount,
        decimal TotalPending,
        decimal DeservedAmount,
        bool IsOverdue,
        List<PaymentRecordDto> Payments,
        List<PurchaseOrderLineResponse> Lines);

    public record PurchaseOrderLineResponse(
        long Id,
        int ProductId,
        string ProductName,
        string? BatchNumber,
        decimal Quantity,
        string Unit,
        decimal UnitPrice,
        bool IsTaxInclusive,
        decimal LineSubtotal,
        decimal LineVatAmount,
        decimal LineManufacturingTaxAmount,
        decimal LineTotal,
        decimal RefundedQuantity);
}
