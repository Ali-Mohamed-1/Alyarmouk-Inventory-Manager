using Inventory.Domain.Entities;

namespace Inventory.Application.DTOs.SalesOrder
{
    public record UpdateSalesOrderPaymentRequest
    {
        public long OrderId { get; init; }
        public PaymentStatus PaymentStatus { get; init; }
        public PaymentMethod? PaymentMethod { get; init; } // Allow updating payment method
        public bool? CheckReceived { get; init; }
        public DateTimeOffset? CheckReceivedDate { get; init; }
        public bool? CheckCashed { get; init; }
        public DateTimeOffset? CheckCashedDate { get; init; }
        public string? TransferId { get; init; }
        public string? Note { get; init; }
    }
}
