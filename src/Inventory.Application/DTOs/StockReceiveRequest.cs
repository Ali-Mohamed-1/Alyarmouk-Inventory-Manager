using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Inventory.Application.DTOs
{
    public class StockReceiveRequest
    {
        public int ProductId { get; set; }
        public decimal Quantity { get; set; }
        public string? BatchNumber { get; set; }
        public long? ProductBatchId { get; set; }
        public string? Note { get; set; }
    }
}
