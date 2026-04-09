using Inventory.Application.DTOs.Payment;
using Inventory.Domain.Entities;

namespace Inventory.Application.DTOs.SupplierSalesOrder
{
    public record SupplierSalesOrderResponseDto
    {
        public long Id { get; init; }
        public string OrderNumber { get; init; } = "";
        public int SupplierId { get; init; }
        public string SupplierName { get; init; } = "";
        public DateTimeOffset CreatedUtc { get; init; }
        public DateTimeOffset OrderDate { get; init; }
        public DateTimeOffset DueDate { get; init; }
        public SalesOrderStatus Status { get; init; }
        public PaymentMethod PaymentMethod { get; init; }
        public PaymentStatus PaymentStatus { get; init; }

        public decimal TotalAmount { get; init; }
        public decimal TotalPaid { get; init; }
        public decimal TotalRefunded { get; init; }
        public decimal NetCash { get; init; }
        public decimal PendingAmount { get; init; }
        public decimal RefundDue { get; init; }

        public bool? CheckReceived { get; init; }
        public DateTimeOffset? CheckReceivedDate { get; init; }
        public bool? CheckCashed { get; init; }
        public DateTimeOffset? CheckCashedDate { get; init; }
        public string? TransferId { get; init; }

        public string? Note { get; init; }
        public bool IsTaxInclusive { get; init; }
        public bool ApplyVat { get; init; }
        public bool ApplyManufacturingTax { get; init; }
        public decimal Subtotal { get; init; }
        public decimal VatAmount { get; init; }
        public decimal ManufacturingTaxAmount { get; init; }
        
        public bool IsHistorical { get; init; }
        public bool IsStockProcessed { get; init; }
        public decimal RefundedAmount { get; init; }

        public List<SupplierSalesOrderLineResponseDto> Lines { get; init; } = new();
        public List<PaymentRecordDto> Payments { get; init; } = new();
    }

    public record SupplierSalesOrderLineResponseDto
    {
        public long Id { get; init; }
        public int ProductId { get; init; }
        public string ProductName { get; init; } = "";
        public decimal Quantity { get; init; }
        public string Unit { get; init; } = "";
        public decimal UnitPrice { get; init; }
        public string? BatchNumber { get; init; }
        public long? ProductBatchId { get; init; }
        public decimal LineSubtotal { get; init; }
        public decimal LineVatAmount { get; init; }
        public decimal LineManufacturingTaxAmount { get; init; }
        public decimal LineTotal { get; init; }
        public decimal RefundedQuantity { get; init; }
    }
}
