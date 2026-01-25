using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Inventory.Domain.Entities
{
    public class PurchaseOrder
    {
        public long Id { get; set; }
        public string OrderNumber { get; set; } = "";
        public int SupplierId { get; set; }
        public Supplier? Supplier { get; set; }
        public string SupplierNameSnapshot { get; set; } = "";
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
        public string CreatedByUserId { get; set; } = "";
        public string CreatedByUserDisplayName { get; set; } = "";
        public string? Note { get; set; }

        // Tax-related fields
        public bool IsTaxInclusive { get; set; } = true; // Whether supplier price includes taxes
        public decimal Subtotal { get; set; } // Base amount before taxes
        public decimal VatAmount { get; set; } // VAT amount (14%)
        public decimal ManufacturingTaxAmount { get; set; } // Manufacturing tax amount (1%)
        public decimal ReceiptExpenses { get; set; } // Additional expenses (e.g., shipping, handling)
        public decimal TotalAmount { get; set; } // Final total including all taxes and expenses

        public List<PurchaseOrderLine> Lines { get; set; } = new();
    }

    public class PurchaseOrderLine
    {
        public long Id { get; set; }
        public long PurchaseOrderId { get; set; }
        public PurchaseOrder? PurchaseOrder { get; set; }
        public int ProductId { get; set; }
        public Product? Product { get; set; }
        public string ProductNameSnapshot { get; set; } = "";
        public decimal Quantity { get; set; }
        public string UnitSnapshot { get; set; } = "";
        public decimal UnitPrice { get; set; } // Price per unit (may be tax-inclusive or exclusive)
        public bool IsTaxInclusive { get; set; } = true; // Inherited from order, but can be overridden
        public decimal LineSubtotal { get; set; } // Base amount for this line
        public decimal LineVatAmount { get; set; } // VAT for this line
        public decimal LineManufacturingTaxAmount { get; set; } // Manufacturing tax for this line
        public decimal LineTotal { get; set; } // Total for this line including taxes
    }
}
