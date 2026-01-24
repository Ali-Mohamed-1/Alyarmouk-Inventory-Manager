using Inventory.Domain.Entities;

namespace Inventory.Application.DTOs.SalesOrder
{
    public record UpdateSalesOrderStatusRequest
    {
        public SalesOrderStatus Status { get; init; }
    }
}
