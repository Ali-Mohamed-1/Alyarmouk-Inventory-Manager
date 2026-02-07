using Inventory.Domain.Entities;

namespace Inventory.Application.DTOs.SalesOrder
{
    public record UpdateSalesOrderStatusRequest
    {
        public long OrderId { get; init; }
        public SalesOrderStatus Status { get; init; }
    }
}
