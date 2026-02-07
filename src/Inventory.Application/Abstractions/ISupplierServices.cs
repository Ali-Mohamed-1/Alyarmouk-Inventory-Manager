using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.Supplier;

namespace Inventory.Application.Abstractions
{
    public interface ISupplierServices
    {
        Task<IEnumerable<SupplierResponse>> GetAllAsync(CancellationToken ct = default);
        Task<SupplierResponse?> GetByIdAsync(int id, CancellationToken ct = default);
        Task<int> CreateAsync(CreateSupplierRequest req, UserContext user, CancellationToken ct = default);
        Task UpdateAsync(int id, UpdateSupplierRequest req, UserContext user, CancellationToken ct = default);
        Task SetActiveAsync(int id, bool isActive, UserContext user, CancellationToken ct = default);
        Task<IEnumerable<SupplierDropdownResponse>> GetForDropdownAsync(CancellationToken ct = default);
        Task<IEnumerable<SupplierProductResponse>> GetSupplierProductsAsync(int supplierId, CancellationToken ct = default);
        Task UpdateSupplierProductsAsync(int supplierId, List<int> productIds, UserContext user, CancellationToken ct = default);
    }

    public record SupplierProductResponse(
        int ProductId,
        string Sku,
        string Name,
        string Unit,
        decimal? LastUnitPrice,
        string? LastBatchNumber,
        DateTimeOffset? LastPurchasedUtc);
}
