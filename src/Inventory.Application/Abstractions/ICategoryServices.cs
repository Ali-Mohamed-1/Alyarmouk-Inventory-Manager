using Inventory.Application.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Inventory.Application.Abstractions
{
    public interface ICategoryServices
    {
        /// <summary>
        /// Retrieves every category so callers can display the full list
        /// </summary>
        Task<IReadOnlyList<CategoryResponseDto>> GetAllAsync(CancellationToken ct = default);

        /// <summary>
        /// Looks up a single category by its id
        /// </summary>
        Task<CategoryResponseDto?> GetByIdAsync(int id, CancellationToken ct = default);

        /// <summary>
        /// Creates a new category using the provided details and caller context
        /// </summary>
        Task<int> CreateAsync(CreateCategoryRequest req, UserContext user, CancellationToken ct = default);

        /// <summary>
        /// Updates an existing category
        /// </summary>
        Task UpdateAsync(int id, UpdateCategoryRequest req, UserContext user, CancellationToken ct = default);
    }
}
