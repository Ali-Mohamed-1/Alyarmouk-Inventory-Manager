using Inventory.Domain.Entities;

namespace Inventory.Application.DTOs.SalesOrder
{
    public record SalesOrderResponseDto
    {
        public long Id { get; init; }
        public string OrderNumber { get; init; } = string.Empty;
        public int CustomerId { get; init; }
        public string CustomerName { get; init; } = string.Empty; // Mapped from CustomerNameSnapshot

        public DateTimeOffset CreatedUtc { get; init; }

        /// <summary>
        /// Order business date.
        /// </summary>
        public DateTimeOffset OrderDate { get; init; }

        /// <summary>
        /// When the customer is expected to pay.
        /// </summary>
        public DateTimeOffset DueDate { get; init; }

        public SalesOrderStatus Status { get; init; }

        public PaymentMethod PaymentMethod { get; init; }
        public PaymentStatus PaymentStatus { get; init; }

        public bool? CheckReceived { get; init; }
        public DateTimeOffset? CheckReceivedDate { get; init; }
        public bool? CheckCashed { get; init; }
        public DateTimeOffset? CheckCashedDate { get; init; }

        /// <summary>
        /// Path or identifier of the Invoice PDF attachment for this order, if any.
        /// </summary>
        public string? InvoicePath { get; init; }
        public DateTimeOffset? InvoiceUploadedUtc { get; init; }

        /// <summary>
        /// Path or identifier of the Receipt PDF attachment for this order, if any.
        /// </summary>
        public string? ReceiptPath { get; init; }
        public DateTimeOffset? ReceiptUploadedUtc { get; init; }

        public string CreatedByUserDisplayName { get; init; } = string.Empty;
        public string? Note { get; init; }

        public bool IsTaxInclusive { get; init; }
        public bool ApplyVat { get; init; }
        public bool ApplyManufacturingTax { get; init; }
        public decimal Subtotal { get; init; }
        public decimal VatAmount { get; init; }
        public decimal ManufacturingTaxAmount { get; init; }
        public decimal TotalAmount { get; init; }

        // Nested list of order items
        public List<SalesOrderLineResponseDto> Lines { get; init; } = new();
    }
}
