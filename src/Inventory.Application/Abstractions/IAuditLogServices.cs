using Inventory.Application.DTOs;

namespace Inventory.Application.Abstractions
{
    public interface IAuditLogServices
    {
        /// <summary>
        /// Pulls the most recent audit entries so we can quickly review what just happened
        /// </summary>
        Task<IReadOnlyList<AuditLogResponseDto>> GetRecentAsync(int take = 50, CancellationToken ct = default);

        /// <summary>
        /// Lists every audit entry for a given entity type
        /// </summary>
        Task<IReadOnlyList<AuditLogResponseDto>> GetByEntityTypeAsync(string entityType, CancellationToken ct = default);

        /// <summary>
        /// Shows the audit trail for a specific entity instance by its id
        /// </summary>
        Task<IReadOnlyList<AuditLogResponseDto>> GetByEntityAsync(string entityType, string entityId, CancellationToken ct = default);

        /// <summary>
        /// Retrieves actions performed by a particular user across the system
        /// </summary>
        Task<IReadOnlyList<AuditLogResponseDto>> GetByUserAsync(string userId, CancellationToken ct = default);

        /// <summary>
        /// Returns audit entries that happened within the provided date range
        /// </summary>
        Task<IReadOnlyList<AuditLogResponseDto>> GetByDateRangeAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default);
    }
}
