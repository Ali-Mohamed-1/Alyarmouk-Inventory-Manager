using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Inventory.Application.DTOs.SalesOrder
{
    public class RefundSalesOrderRequest
    {
        [Required]
        public long OrderId { get; set; }

        /// <summary>
        /// Total monetary amount to refund.
        /// For line-item refunds, this should equal sum of (LineRefunds.Quantity * UnitPrice).
        /// </summary>
        public decimal Amount { get; set; }

        /// <summary>
        /// Optional: Specific line items to refund.
        /// If null or empty, treat as order-level monetary refund.
        /// If populated, return specific items to inventory.
        /// </summary>
        public List<RefundLineItem>? LineItems { get; set; }

        /// <summary>
        /// Reason for refund (for audit trail).
        /// </summary>
        public string? Reason { get; set; }
    }

    public class RefundLineItem
    {
        [Required]
        public long SalesOrderLineId { get; set; }

        [Required]
        [Range(0.01, double.MaxValue)]
        public decimal Quantity { get; set; }

        /// <summary>
        /// Optional: If batch-tracked, specify which batch to return to.
        /// If null, will use the original batch from the sales order line.
        /// </summary>
        public long? ProductBatchId { get; set; }

        public string? BatchNumber { get; set; }
    }
}
