using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

using Inventory.Domain.Entities; // for PurchasePaymentStatus

namespace Inventory.Application.DTOs.PurchaseOrder
{
    public class CreatePurchaseOrderRequest
    {
        [Required]
        public int SupplierId { get; set; }

        public string? Note { get; set; }

        public DateTimeOffset? DueDate { get; set; }
        public bool ConnectToReceiveStock { get; set; } = true;

        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash; // Default to Cash
        public PurchasePaymentStatus PaymentStatus { get; set; } = PurchasePaymentStatus.Unpaid; // Default to Unpaid

        // Tax Configuration
        public bool IsTaxInclusive { get; set; } = false;
        public bool ApplyVat { get; set; } = true;
        public bool ApplyManufacturingTax { get; set; } = true;

        public decimal ReceiptExpenses { get; set; }

        [Required]
        [MinLength(1, ErrorMessage = "At least one line item is required.")]
        public List<CreatePurchaseOrderLineRequest> Lines { get; set; } = new();
    }

    public class CreatePurchaseOrderLineRequest
    {
        [Required]
        public int ProductId { get; set; }

        /// <summary>
        /// Optional batch/lot number for the stock being received on this line.
        /// </summary>
        public string? BatchNumber { get; set; }

        [Range(0.0001, double.MaxValue, ErrorMessage = "Quantity must be greater than 0.")]
        public decimal Quantity { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "Unit price cannot be negative.")]
        public decimal UnitPrice { get; set; }
    }
}
