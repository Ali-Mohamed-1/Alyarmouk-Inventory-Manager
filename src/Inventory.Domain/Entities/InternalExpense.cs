using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Inventory.Domain.Entities
{
    public class InternalExpense
    {
        public long Id { get; set; }
        public string ExpenseType { get; set; } = ""; // e.g., "Rent", "Utilities", "Insurance"
        public string Description { get; set; } = "";
        public decimal Amount { get; set; }
        public DateTimeOffset ExpenseDate { get; set; }
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
        public string CreatedByUserId { get; set; } = "";
        public string CreatedByUserDisplayName { get; set; } = "";
        public string? Note { get; set; }
    }
}
