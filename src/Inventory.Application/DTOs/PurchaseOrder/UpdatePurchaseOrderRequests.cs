using System;
using Inventory.Domain.Entities;

namespace Inventory.Application.DTOs.PurchaseOrder
{
    public class UpdatePurchaseOrderPaymentRequest
    {
        public long OrderId { get; set; }
        public PurchasePaymentStatus PaymentStatus { get; set; }
    }

    public class RefundPurchaseOrderRequest
    {
        public long OrderId { get; set; }
        public decimal Amount { get; set; }
        public string? Reason { get; set; }
    }
}
