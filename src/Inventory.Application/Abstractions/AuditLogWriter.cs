using Inventory.Application.DTOs;
using Inventory.Domain.Entities;
using System.Threading;
using System.Threading.Tasks;

namespace Inventory.Application.Abstractions
{
    public interface IAuditLogWriter
    {
        /// <summary>
        /// Records a Create action for an entity with an integer ID
        /// </summary>
        Task LogCreateAsync<T>(object entityId, UserContext user, object? afterState = null, CancellationToken ct = default) where T : class;

        /// <summary>
        /// Records an Update action, capturing before and after states
        /// </summary>
        Task LogUpdateAsync<T>(object entityId, UserContext user, object? beforeState = null, object? afterState = null, CancellationToken ct = default) where T : class;

        /// <summary>
        /// Records a Delete action for an entity with a long ID
        /// </summary>
        Task LogDeleteAsync<T>(object entityId, UserContext user, object? beforeState = null, CancellationToken ct = default) where T : class;

        /// <summary>
        /// Create a custom audit log entry
        /// </summary>
        Task LogAsync(string entityType, string entityId, AuditAction action, UserContext user, string? changesJson = null, CancellationToken ct = default);
    }
}