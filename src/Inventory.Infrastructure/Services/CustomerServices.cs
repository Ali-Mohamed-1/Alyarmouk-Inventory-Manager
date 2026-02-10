using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.Customer;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Inventory.Application.Exceptions;


namespace Inventory.Infrastructure.Services
{
    public sealed class CustomerServices : ICustomerServices
    {
        private const int MaxNameLength = 200; // Matches DB constraint
        private const int MaxPhoneLength = 50;
        private const int MaxEmailLength = 200;
        private const int MaxSearchTake = 100; // Prevent excessive queries
        private readonly AppDbContext _db;

        public CustomerServices(AppDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<IReadOnlyList<CustomerResponseDto>> GetAllAsync(CancellationToken ct = default)
        {
            return await _db.Customers
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .Select(c => new CustomerResponseDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    Phone = c.Phone,
                    Email = c.Email,
                    Address = c.Address,
                    CreatedUtc = c.CreatedUtc
                })
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<CustomerResponseDto>> GetForDropdownAsync(CancellationToken ct = default)
        {
            // Optimized: Only select Id and Name from database (dropdowns don't need other fields)
            // Other properties are set to defaults to satisfy the DTO contract
            return await _db.Customers
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .Select(c => new CustomerResponseDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    Phone = null,
                    Email = null,
                    CreatedUtc = c.CreatedUtc 
                })
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<CustomerResponseDto>> SearchByNameAsync(string name, int take = 10, CancellationToken ct = default)
        {
            var searchTerm = (name ?? string.Empty).Trim();

            // If search term is empty, return empty list (don't return everything)
            if (string.IsNullOrWhiteSpace(searchTerm))
                return Array.Empty<CustomerResponseDto>();

            // Clamp take to prevent excessive queries
            take = Math.Clamp(take, 1, MaxSearchTake);

            return await _db.Customers
                .AsNoTracking()
                .Where(c => c.Name.Contains(searchTerm))
                .OrderBy(c => c.Name)
                .Take(take)
                .Select(c => new CustomerResponseDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    Phone = c.Phone,
                    Email = c.Email,
                    Address = c.Address,
                    CreatedUtc = c.CreatedUtc
                })
                .ToListAsync(ct);
        }

        public async Task<CustomerResponseDto?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id), "Id must be positive.");

            return await _db.Customers
                .AsNoTracking()
                .Where(c => c.Id == id)
                .Select(c => new CustomerResponseDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    Phone = c.Phone,
                    Email = c.Email,
                    Address = c.Address,
                    CreatedUtc = c.CreatedUtc
                })
                .SingleOrDefaultAsync(ct);
        }

        public async Task<int> CreateAsync(CreateCustomerRequest req, UserContext user, CancellationToken ct = default)
        {
            if (req is null) throw new ArgumentNullException(nameof(req));
            ValidateUser(user);

            var normalizedName = NormalizeAndValidateName(req.Name);
            var normalizedPhone = NormalizePhone(req.Phone);
            var normalizedEmail = NormalizeAndValidateEmail(req.Email);

            var entity = new Inventory.Domain.Entities.Customer
            {
                Name = normalizedName,
                Phone = normalizedPhone,
                Email = normalizedEmail,
                Address = req.Address,
                CreatedUtc = DateTimeOffset.UtcNow
            };

            _db.Customers.Add(entity);

            // Use transaction to ensure both customer and audit log are saved atomically
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                await _db.SaveChangesAsync(ct);


                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync(ct);
                throw new ConflictException("Could not create customer due to a database conflict.", ex);
            }

            return entity.Id;
        }

        public async Task UpdateAsync(int id, UpdateCustomerRequest req, UserContext user, CancellationToken ct = default)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id), "Id must be positive.");
            if (req is null) throw new ArgumentNullException(nameof(req));
            ValidateUser(user);

            if (req.Id != 0 && req.Id != id)
                throw new ArgumentException("Route id does not match request id.", nameof(req));

            var normalizedName = NormalizeAndValidateName(req.Name);
            var normalizedPhone = NormalizePhone(req.Phone);
            var normalizedEmail = NormalizeAndValidateEmail(req.Email);

            var entity = await _db.Customers
                .SingleOrDefaultAsync(c => c.Id == id, ct);

            if (entity is null)
                throw new NotFoundException($"Customer id {id} was not found.");

            // AUDIT LOG: Capture BEFORE state
            var beforeState = new
            {
                Name = entity.Name,
                Phone = entity.Phone,
                Email = entity.Email,
                Address = entity.Address
            };

            // Update the entity
            entity.Name = normalizedName;
            entity.Phone = normalizedPhone;
            entity.Email = normalizedEmail;
            entity.Address = req.Address;

            // AUDIT LOG: Capture AFTER state
            var afterState = new
            {
                Name = entity.Name,
                Phone = entity.Phone,
                Email = entity.Email,
                Address = entity.Address
            };

            // Use transaction to ensure both customer update and audit log are saved atomically
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                await _db.SaveChangesAsync(ct);


                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync(ct);
                throw new ConflictException("Could not update customer due to a database conflict.", ex);
            }
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

            if (normalized.Length == 0)
                throw new ValidationException("Customer name is required.");

            if (normalized.Length > MaxNameLength)
                throw new ValidationException($"Customer name must be <= {MaxNameLength} characters.");

            return normalized;
        }

        private static string? NormalizePhone(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone))
                return null;

            var normalized = phone.Trim();

            if (normalized.Length > MaxPhoneLength)
                throw new ValidationException($"Phone number must be <= {MaxPhoneLength} characters.");

            return normalized.Length > 0 ? normalized : null;
        }

        private static string? NormalizeAndValidateEmail(string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return null;

            var normalized = email.Trim().ToLowerInvariant();

            if (normalized.Length > MaxEmailLength)
                throw new ValidationException($"Email must be <= {MaxEmailLength} characters.");

            // Basic email format validation
            // Note: [EmailAddress] attribute in DTO provides client-side validation
            // This is server-side validation for security
            var emailRegex = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase);
            if (!emailRegex.IsMatch(normalized))
                throw new ValidationException("Email format is invalid.");

            return normalized;
        }

        #endregion
    }
}
