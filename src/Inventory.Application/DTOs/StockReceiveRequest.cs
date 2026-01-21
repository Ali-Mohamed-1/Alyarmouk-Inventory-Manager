using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Inventory.Application.DTOs
{
    public record StockReceiveRequest
    (
        int productId,
        decimal Quantitiy,
        string? Note
    );
}
