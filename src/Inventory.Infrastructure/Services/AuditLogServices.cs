using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Inventory.Application.Exceptions;


namespace Inventory.Infrastructure.Services
{
    public sealed class AuditLogServices : IAuditLogServices
    {
        private const int MaxTake = 1000;
        private readonly AppDbContext _db;

        public AuditLogServices(AppDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<IReadOnlyList<AuditLogResponseDto>> GetRecentAsync(int take = 50, CancellationToken ct = default)
        {
            take = Math.Clamp(take, 1, MaxTake);

            return await _db.AuditLogs
                .AsNoTracking()
                .OrderByDescending(a => a.TimestampUtc)
                .Take(take)
                .Select(a => new AuditLogResponseDto
                {
                    Id = a.Id,
                    EntityType = a.EntityType,
                    EntityId = a.EntityId,
                    Action = a.Action.ToString(),
                    TimestampUtc = a.TimestampUtc,
                    UserDisplayName = a.UserDisplayName,
                    ChangesJson = a.ChangesJson
                })
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<AuditLogResponseDto>> GetByEntityTypeAsync(string entityType, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(entityType))
                return Array.Empty<AuditLogResponseDto>();

            return await _db.AuditLogs
                .AsNoTracking()
                .Where(a => a.EntityType == entityType)
                .OrderByDescending(a => a.TimestampUtc)
                .Select(a => new AuditLogResponseDto
                {
                    Id = a.Id,
                    EntityType = a.EntityType,
                    EntityId = a.EntityId,
                    Action = a.Action.ToString(),
                    TimestampUtc = a.TimestampUtc,
                    UserDisplayName = a.UserDisplayName,
                    ChangesJson = a.ChangesJson
                })
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<AuditLogResponseDto>> GetByEntityAsync(string entityType, string entityId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(entityType) || string.IsNullOrWhiteSpace(entityId))
                return Array.Empty<AuditLogResponseDto>();

            return await _db.AuditLogs
                .AsNoTracking()
                .Where(a => a.EntityType == entityType && a.EntityId == entityId)
                .OrderByDescending(a => a.TimestampUtc)
                .Select(a => new AuditLogResponseDto
                {
                    Id = a.Id,
                    EntityType = a.EntityType,
                    EntityId = a.EntityId,
                    Action = a.Action.ToString(),
                    TimestampUtc = a.TimestampUtc,
                    UserDisplayName = a.UserDisplayName,
                    ChangesJson = a.ChangesJson
                })
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<AuditLogResponseDto>> GetByUserAsync(string userId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return Array.Empty<AuditLogResponseDto>();

            return await _db.AuditLogs
                .AsNoTracking()
                .Where(a => a.UserId == userId)
                .OrderByDescending(a => a.TimestampUtc)
                .Select(a => new AuditLogResponseDto
                {
                    Id = a.Id,
                    EntityType = a.EntityType,
                    EntityId = a.EntityId,
                    Action = a.Action.ToString(),
                    TimestampUtc = a.TimestampUtc,
                    UserDisplayName = a.UserDisplayName,
                    ChangesJson = a.ChangesJson
                })
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<AuditLogResponseDto>> GetByDateRangeAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
        {
            if (start > end)
                throw new ArgumentException("Start date must be before or equal to end date.", nameof(start));

            return await _db.AuditLogs
                .AsNoTracking()
                .Where(a => a.TimestampUtc >= start && a.TimestampUtc <= end)
                .OrderByDescending(a => a.TimestampUtc)
                .Select(a => new AuditLogResponseDto
                {
                    Id = a.Id,
                    EntityType = a.EntityType,
                    EntityId = a.EntityId,
                    Action = a.Action.ToString(),
                    TimestampUtc = a.TimestampUtc,
                    UserDisplayName = a.UserDisplayName,
                    ChangesJson = a.ChangesJson
                })
                .ToListAsync(ct);
        }
    }
}