using Inventory.Domain.Entities;

namespace Inventory.Application.DTOs.PurchaseOrder
{
    public record UpdatePurchaseOrderPaymentRequest
    {
        public long OrderId { get; init; }
        public PaymentMethod? PaymentMethod { get; init; }
        public bool CheckReceived { get; init; } = false;
        public DateTimeOffset? CheckReceivedDate { get; init; }
        public bool CheckCashed { get; init; } = false;
        public DateTimeOffset? CheckCashedDate { get; init; }
        public string? TransferId { get; init; }
        public string? Note { get; init; }
    }
}
