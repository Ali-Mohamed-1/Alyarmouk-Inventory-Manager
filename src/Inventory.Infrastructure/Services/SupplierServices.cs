using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.Supplier;
using Inventory.Domain.Entities;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Services
{
    public sealed class SupplierServices : ISupplierServices
    {
        private readonly AppDbContext _db;
        private readonly IAuditLogWriter _auditWriter;

        public SupplierServices(AppDbContext db, IAuditLogWriter auditWriter)
        {
            _db = db;
            _auditWriter = auditWriter;
        }

        public async Task<IEnumerable<SupplierResponse>> GetAllAsync(CancellationToken ct = default)
        {
            return await _db.Suppliers
                .AsNoTracking()
                .OrderByDescending(s => s.CreatedUtc)
                .Select(s => MapToResponse(s))
                .ToListAsync(ct);
        }

        public async Task<SupplierResponse?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            var supplier = await _db.Suppliers
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == id, ct);

            return supplier == null ? null : MapToResponse(supplier);
        }

        public async Task<int> CreateAsync(CreateSupplierRequest req, UserContext user, CancellationToken ct = default)
        {
            var supplier = new Supplier
            {
                Name = req.Name,
                Phone = req.Phone,
                Email = req.Email,
                Address = req.Address,
                CreatedUtc = DateTimeOffset.UtcNow,
                IsActive = true
            };

            _db.Suppliers.Add(supplier);
            await _db.SaveChangesAsync(ct);

            await _auditWriter.LogCreateAsync<Supplier>(supplier.Id, user, supplier, ct);

            return supplier.Id;
        }

        public async Task UpdateAsync(int id, UpdateSupplierRequest req, UserContext user, CancellationToken ct = default)
        {
            var supplier = await _db.Suppliers.FindAsync(new object[] { id }, ct);
            if (supplier == null) throw new Exception("Supplier not found");

            var before = new { supplier.Name, supplier.Phone, supplier.Email, supplier.Address, supplier.IsActive };

            supplier.Name = req.Name;
            supplier.Phone = req.Phone;
            supplier.Email = req.Email;
            supplier.Address = req.Address;
            supplier.IsActive = req.IsActive;

            await _db.SaveChangesAsync(ct);

            await _auditWriter.LogUpdateAsync<Supplier>(id, user, before, supplier, ct);
        }

        public async Task SetActiveAsync(int id, bool isActive, UserContext user, CancellationToken ct = default)
        {
            var supplier = await _db.Suppliers.FindAsync(new object[] { id }, ct);
            if (supplier == null) throw new Exception("Supplier not found");

            var before = new { supplier.IsActive };
            supplier.IsActive = isActive;

            await _db.SaveChangesAsync(ct);

            await _auditWriter.LogUpdateAsync<Supplier>(id, user, before, new { IsActive = isActive }, ct);
        }

        public async Task<IEnumerable<SupplierDropdownResponse>> GetForDropdownAsync(CancellationToken ct = default)
        {
            return await _db.Suppliers
                .AsNoTracking()
                .Where(s => s.IsActive)
                .OrderBy(s => s.Name)
                .Select(s => new SupplierDropdownResponse(s.Id, s.Name))
                .ToListAsync(ct);
        }

        private static SupplierResponse MapToResponse(Supplier s)
        {
            return new SupplierResponse(
                s.Id,
                s.Name,
                s.Phone,
                s.Email,
                s.Address,
                s.IsActive,
                s.CreatedUtc);
        }
    }
}
