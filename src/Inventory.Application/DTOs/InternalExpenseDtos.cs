using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Inventory.Application.DTOs
{
    public record CreateInternalExpenseRequest(
        string ExpenseType,
        string Description,
        decimal Amount,
        DateTimeOffset ExpenseDate,
        string? Note);

    public record InternalExpenseResponse(
        long Id,
        string ExpenseType,
        string Description,
        decimal Amount,
        DateTimeOffset ExpenseDate,
        string CreatedByUserDisplayName,
        string? Note);
}
