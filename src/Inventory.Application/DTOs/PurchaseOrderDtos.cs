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

    public record PurchaseOrderResponse
    {
        public long Id { get; init; }
        public string OrderNumber { get; init; } = "";
        public int SupplierId { get; init; }
        public string SupplierName { get; init; } = "";
        public DateTimeOffset CreatedUtc { get; init; }
        public DateTimeOffset? PaymentDeadline { get; init; }
        public PurchaseOrderStatus Status { get; init; }
        public PurchasePaymentStatus PaymentStatus { get; init; }
        public string CreatedByUserDisplayName { get; init; } = "";
        public bool IsTaxInclusive { get; init; }
        public bool ApplyVat { get; init; }
        public bool ApplyManufacturingTax { get; init; }
        public decimal Subtotal { get; init; }
        public decimal VatAmount { get; init; }
        public decimal ManufacturingTaxAmount { get; init; }
        public decimal ReceiptExpenses { get; init; }
        public decimal TotalAmount { get; init; }
        public decimal RefundedAmount { get; init; }
        public string? Note { get; init; }
        public bool IsHistorical { get; init; }
        public bool IsStockProcessed { get; init; }
        public string? InvoicePath { get; init; }
        public DateTimeOffset? InvoiceUploadedUtc { get; init; }
        public string? ReceiptPath { get; init; }
        public DateTimeOffset? ReceiptUploadedUtc { get; init; }
        public PaymentMethod PaymentMethod { get; init; }
        public bool? CheckReceived { get; init; }
        public DateTimeOffset? CheckReceivedDate { get; init; }
        public bool? CheckCashed { get; init; }
        public DateTimeOffset? CheckCashedDate { get; init; }
        public string? TransferId { get; init; }
        public decimal PaidAmount { get; init; }
        public decimal RemainingAmount { get; set; }
        public decimal DeservedAmount { get; init; }
        public bool IsOverdue { get; init; }
        public List<PaymentRecordDto> Payments { get; init; } = new();
        public List<PurchaseOrderLineResponse> Lines { get; init; } = new();

        public decimal TotalPaid { get; set; }
        public decimal TotalRefunded { get; set; }
        public decimal NetCash { get; set; }
        public decimal PendingAmount { get; set; }
        public decimal RefundDue { get; set; }
    }

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
