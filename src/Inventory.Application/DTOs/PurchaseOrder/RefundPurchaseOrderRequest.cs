using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Inventory.Application.DTOs.PurchaseOrder
{
    public class RefundPurchaseOrderRequest
    {
        [Required]
        public long OrderId { get; set; }

        /// <summary>
        /// Total monetary amount to refund.
        /// Must be positive.
        /// </summary>
        public decimal Amount { get; set; }

        /// <summary>
        /// Optional: Specific line items to refund (return to supplier).
        /// If populated, stock will be deducted (reversed).
        /// </summary>
        public List<RefundPurchaseLineItem>? LineItems { get; set; }

        /// <summary>
        /// Reason for refund (for audit trail).
        /// </summary>
        public string? Reason { get; set; }
    }

    public class RefundPurchaseLineItem
    {
        [Required]
        public long PurchaseOrderLineId { get; set; }

        [Required]
        [Range(0.01, double.MaxValue)]
        public decimal Quantity { get; set; }
        
        /// <summary>
        /// Optional: If batch number is known/required for verification.
        /// </summary>
        public string? BatchNumber { get; set; }
    }
}
