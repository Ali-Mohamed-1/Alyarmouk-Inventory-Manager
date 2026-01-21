using Inventory.Application.DTOs;
using Inventory.Application.DTOs.Product;

namespace Inventory.Application.Abstractions
{
    public interface IProductServices
    {
        /// <summary>
        /// Retrieves all products
        /// </summary>
        Task<IReadOnlyList<ProductResponseDto>> GetAllAsync(CancellationToken ct = default);

        /// <summary>
        /// Finds a single product by id
        /// </summary>
        Task<ProductResponseDto?> GetByIdAsync(int id, CancellationToken ct = default);

        /// <summary>
        /// Adds a new product
        /// </summary>
        Task<int> CreateAsync(CreateProductRequest req, UserContext user, CancellationToken ct = default);

        /// <summary>
        /// Updates an existing product
        /// </summary>
        Task UpdateAsync(int id, UpdateProductRequest req, UserContext user, CancellationToken ct = default);

        /// <summary>
        /// Toggles whether a product is active
        /// </summary>
        Task SetActiveAsync(int id, bool isActive, UserContext user, CancellationToken ct = default);
    }
}
