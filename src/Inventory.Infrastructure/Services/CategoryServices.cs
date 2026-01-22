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
        private readonly AppDbContext _db;
        public CategoryServices(AppDbContext db)
        {
            _db = db;
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
            if (req is null) throw new ArgumentNullException(nameof(req));
            ValidateUser(user);

            var entity = await _db.categories.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (entity is null)
                throw new KeyNotFoundException($"Category with id '{id}' was not found.");

            string normalizedName = NormalizeAndValidateName(req.Name);

            if (entity.Name != normalizedName)
            {
                var duplicateName = await _db.categories
                    .AnyAsync(c => c.Id != id && c.Name.ToLower() == normalizedName.ToLower(), ct);

                if (duplicateName)
                    throw new ValidationException($"A category with the name '{normalizedName}' already exists.");

                entity.Name = normalizedName;
            }

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

            if (normalized.Length > 100)
                throw new ValidationException($"Category name must be <= 100 characters.");

            return normalized;
        }

        #endregion
    }
}
