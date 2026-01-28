using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.PurchaseOrder;
using Inventory.Domain.Entities;

namespace Inventory.Application.Abstractions
{
    public interface IPurchaseOrderServices
    {
        Task<IEnumerable<PurchaseOrderResponse>> GetRecentAsync(int count = 10, CancellationToken ct = default);
        Task<PurchaseOrderResponse?> GetByIdAsync(long id, CancellationToken ct = default);
        Task<long> CreateAsync(CreatePurchaseOrderRequest req, UserContext user, CancellationToken ct = default);
        Task UpdateStatusAsync(long id, PurchaseOrderStatus status, UserContext user, CancellationToken ct = default);
    }
}

