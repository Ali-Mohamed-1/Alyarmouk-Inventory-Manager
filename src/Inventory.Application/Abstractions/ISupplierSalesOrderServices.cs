using Inventory.Application.DTOs;
using Inventory.Application.DTOs.SupplierSalesOrder;
using Inventory.Domain.Entities;

namespace Inventory.Application.Abstractions
{
    public interface ISupplierSalesOrderServices
    {
        Task<long> CreateAsync(CreateSupplierSalesOrderRequest req, UserContext user, CancellationToken ct = default);
        Task<SupplierSalesOrderResponseDto?> GetByIdAsync(long id, CancellationToken ct = default);
        Task<IReadOnlyList<SupplierSalesOrderResponseDto>> GetRecentAsync(int take = 50, CancellationToken ct = default);
        Task CancelAsync(long orderId, UserContext user, CancellationToken ct = default);
        Task AddPaymentAsync(long orderId, Inventory.Application.DTOs.Payment.CreatePaymentRequest req, UserContext user, CancellationToken ct = default);
        Task RefundAsync(RefundSupplierSalesOrderRequest req, UserContext user, CancellationToken ct = default);
    }
}
