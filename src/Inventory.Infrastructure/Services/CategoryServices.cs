using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Inventory.Infrastructure.Services
{
    public class CategoryServices : ICategoryServices
    {
        private const int MaxName = 100;
        private readonly AppDbContext _db;
        private readonly IAuditLogWriter _auditWriter;
        public CategoryServices(AppDbContext db, IAuditLogWriter auditWriter)
        {
            _db = db;
            _auditWriter = auditWriter;
        }
        public async Task<int> CreateAsync(CreateCategoryRequest req, UserContext user, CancellationToken ct = default)
        {
            if (req is null) throw new ArgumentNullException(nameof(req));
            ValidateUser(user);

            string normalizedName = NormalizeAndValidateName(req.Name);

            var exists = await _db.categories
                .AsNoTracking()
                .AnyAsync(c => c.Name.ToUpper() == normalizedName.ToUpper(), ct);

            if (exists)
                throw new Exception($"A category with the name '{normalizedName}' already exists.");

            var category = new Domain.Entities.Category { Name = normalizedName };
            
            _db.categories.Add(category);

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                throw new ConflictException("Could not update category due to a database conflict.", ex);
            }

            await _auditWriter.LogCreateAsync<Inventory.Domain.Entities.Category>(
                category.Id,
                user,
                afterState: new { Name = category.Name },
                ct
            );

            await _db.SaveChangesAsync(ct);

            return category.Id;
        }

        public async Task<IReadOnlyList<CategoryResponseDto>> GetAllAsync(CancellationToken ct = default)
        {
            return await _db.categories
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .Select(c => new CategoryResponseDto
                {
                    Id = c.Id,
                    Name = c.Name
                })
                .ToListAsync(ct);
        }

        public async Task<CategoryResponseDto?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            return await _db.categories
                .AsNoTracking()
                .Where(c => c.Id == id)
                .Select(c => new CategoryResponseDto
                {
                    Id = c.Id,
                    Name = c.Name
                })
                .SingleOrDefaultAsync(ct);
        }

        public async Task UpdateAsync(int id, UpdateCategoryRequest req, UserContext user, CancellationToken ct = default)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id), "Id must be positive.");
            if (req is null) throw new ArgumentNullException(nameof(req));
            ValidateUser(user);

            if (req.Id != 0 && req.Id != id)
                throw new ArgumentException("Route id does not match request id.", nameof(req));

            var normalizedName = NormalizeAndValidateName(req.Name);

            var entity = await _db.categories
                .SingleOrDefaultAsync(c => c.Id == id, ct);

            if (entity is null)
                throw new NotFoundException($"Category id {id} was not found.");

            var beforeState = new { Name = entity.Name };

            var duplicateName = await _db.categories
                .AsNoTracking()
                .AnyAsync(c => c.Id != id && c.Name.ToUpper() == normalizedName.ToUpper(), ct);

            if (duplicateName)
                throw new ConflictException($"Category name '{normalizedName}' already exists.");

            entity.Name = normalizedName;

            var afterState = new { Name = entity.Name };

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                throw new ConflictException("Could not update category due to a database conflict.", ex);
            }

            await _auditWriter.LogUpdateAsync<Inventory.Domain.Entities.Category>(
                entity.Id,
                user,
                beforeState: beforeState,
                afterState: afterState,
                ct);

            await _db.SaveChangesAsync(ct);
        }

        #region Helper Methods

        private static void ValidateUser(UserContext user)
        {
            if (user is null) throw new ArgumentNullException(nameof(user));
            if (string.IsNullOrWhiteSpace(user.UserId)) throw new UnauthorizedAccessException("Missing user id.");
            if (string.IsNullOrWhiteSpace(user.UserDisplayName)) throw new UnauthorizedAccessException("Missing user display name.");
        }

        private static string NormalizeAndValidateName(string? name)
        {
            var normalized = (name ?? string.Empty).Trim();

            if (normalized.Length < 2)
                throw new ValidationException("Category name must be at least 2 characters.");

            if (normalized.Length > MaxName)
                throw new ValidationException($"Category name must be <= {MaxName} characters.");

            return normalized;
        }

        #endregion
    }

    public sealed class NotFoundException : Exception
    {
        public NotFoundException(string message) : base(message) { }
    }

    public sealed class ConflictException : Exception
    {
        public ConflictException(string message) : base(message) { }
        public ConflictException(string message, Exception inner) : base(message, inner) { }
    }

    public sealed class ValidationException : Exception
    {
        public ValidationException(string message) : base(message) { }
    }
}
