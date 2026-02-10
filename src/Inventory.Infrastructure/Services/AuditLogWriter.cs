using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Domain.Entities;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Inventory.Application.Exceptions;


namespace Inventory.Infrastructure.Services
{
    public sealed class AuditLogWriter : IAuditLogWriter
    {
        private const int MaxChangesJsonLength = 4000;
        private readonly AppDbContext _db;

        public AuditLogWriter(AppDbContext db)
        {
            _db = db;
        }

        public Task LogCreateAsync<T>(object entityId, UserContext user, object? afterState = null, CancellationToken ct = default) where T : class
        {
            return LogAsync(GetEntityType<T>(), entityId.ToString()!, AuditAction.Create, user, SerializeIfNotNull(afterState), ct);
        }

        public Task LogUpdateAsync<T>(object entityId, UserContext user, object? beforeState = null, object? afterState = null, CancellationToken ct = default) where T : class
        {
            var changesJson = SerializeChanges(beforeState, afterState);
            return LogAsync(GetEntityType<T>(), entityId.ToString()!, AuditAction.Update, user, changesJson, ct);
        }

        public Task LogDeleteAsync<T>(object entityId, UserContext user, object? beforeState = null, CancellationToken ct = default) where T : class
        {
            return LogAsync(GetEntityType<T>(), entityId.ToString()!, AuditAction.Delete, user, SerializeIfNotNull(beforeState), ct);
        }

        public async Task LogAsync(string entityType, string entityId, AuditAction action, UserContext user, string? changesJson = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(entityType)) throw new ArgumentException("Entity type cannot be empty.", nameof(entityType));
            if (string.IsNullOrWhiteSpace(entityId)) throw new ArgumentException("Entity ID cannot be empty.", nameof(entityId));
            if (user is null) throw new ArgumentNullException(nameof(user));
            if (string.IsNullOrWhiteSpace(user.UserId)) throw new ArgumentException("User ID cannot be empty.", nameof(user));

            if (changesJson is not null && changesJson.Length > MaxChangesJsonLength)
            {
                changesJson = changesJson.Substring(0, MaxChangesJsonLength - 3) + "...";
            }

            var auditLog = new AuditLog
            {
                EntityType = entityType,
                EntityId = entityId,
                Action = action,
                TimestampUtc = DateTimeOffset.UtcNow,
                UserId = user.UserId,
                UserDisplayName = user.UserDisplayName,
                ChangesJson = changesJson
            };

            await _db.AuditLogs.AddAsync(auditLog);
        }

        private static string GetEntityType<T>() where T : class
        {
            return typeof(T).Name;
        }

        private static string? SerializeIfNotNull(object? obj)
        {
            if (obj is null) return null;
            try
            {
                return JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = false });
            }
            catch
            {
                return obj.ToString();
            }
        }

        private static string? SerializeChanges(object? before, object? after)
        {
            if (before is null && after is null) return null;

            try
            {
                var changes = new
                {
                    Before = before,
                    After = after
                };
                return JsonSerializer.Serialize(changes, new JsonSerializerOptions { WriteIndented = false });
            }
            catch
            {
                var beforeStr = before?.ToString() ?? "null";
                var afterStr = after?.ToString() ?? "null";
                return $"{{\"Before\":{beforeStr},\"After\":{afterStr}}}";
            }
        }
    }
}