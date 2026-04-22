using System;
using System.Collections.Generic;

namespace Inventory.Application.DTOs.Refunds
{
    public class RefundTransactionResponseDto
    {
        public long Id { get; set; }
        public DateTimeOffset ProcessedUtc { get; set; }
        public decimal Amount { get; set; }
        public string? Reason { get; set; }
        public string? Note { get; set; }
        public string ProcessedByUserDisplayName { get; set; } = "";
        public List<RefundTransactionLineResponseDto> Lines { get; set; } = new();
        
        // Calculated Type for UI (Money only, Quantity only, or Both)
        public string RefundCategory
        {
            get
            {
                bool hasMoney = Amount > 0;
                bool hasQuantity = Lines.Any(l => l.Quantity > 0);
                
                if (hasMoney && hasQuantity) return "Both";
                if (hasQuantity) return "Quantity (Stock Return)";
                return "Monetary Only";
            }
        }
    }

    public class RefundTransactionLineResponseDto
    {
        public long Id { get; set; }
        public int ProductId { get; set; }
        public string ProductName { get; set; } = "";
        public string? BatchNumber { get; set; }
        public decimal Quantity { get; set; }
        public decimal LineRefundAmount { get; set; }
        public decimal SubtotalRefunded { get; set; }
        public decimal VatRefunded { get; set; }
        public decimal ManTaxRefunded { get; set; }
    }
}
