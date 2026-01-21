using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Inventory.Application.DTOs
{
    public sealed record PagedResult<T>
    (
        IReadOnlyList<T> Items,
        int TotalCount,
        int PageNumber,
        int PageSize
    );
}
