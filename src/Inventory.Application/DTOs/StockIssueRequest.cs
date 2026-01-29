using System;

namespace Inventory.Application.DTOs
{
    public class StockIssueRequest
    {
        public int ProductId { get; set; }
        public decimal Quantity { get; set; }
        public string? BatchNumber { get; set; }
        public string? Note { get; set; }
    }
}
