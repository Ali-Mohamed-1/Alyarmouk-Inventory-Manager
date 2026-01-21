using Inventory.Application.DTOs;
using Inventory.Application.DTOs.Customer;

namespace Inventory.Application.Abstractions
{
    public interface ICustomerServices
    {
        /// <summary>
        /// Returns a lightweight list of customers (id and name)
        /// </summary>
        Task<IReadOnlyList<CustomerResponseDto>> GetForDropdownAsync(CancellationToken ct = default);

        /// <summary>
        /// Searches customers by name
        /// </summary>
        Task<IReadOnlyList<CustomerResponseDto>> SearchByNameAsync(string name, int take = 10, CancellationToken ct = default);

        /// <summary>
        /// Returns all custoemrs
        /// </summary>
        Task<IReadOnlyList<CustomerResponseDto>> GetAllAsync(CancellationToken ct = default);

        /// <summary>
        /// Finds a single customer by id
        /// </summary>
        Task<CustomerResponseDto?> GetByIdAsync(int id, CancellationToken ct = default);

        /// <summary>
        /// Registers a new customer 
        /// </summary>
        Task<int> CreateAsync(CreateCustomerRequest req, UserContext user, CancellationToken ct = default);

        /// <summary>
        /// Applies updates to an existing customer record
        /// </summary>
        Task UpdateAsync(int id, UpdateCustomerRequest req, UserContext user, CancellationToken ct = default);
    }
}
