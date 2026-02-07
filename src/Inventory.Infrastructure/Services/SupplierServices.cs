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

        public async Task<IEnumerable<SupplierProductResponse>> GetSupplierProductsAsync(int supplierId, CancellationToken ct = default)
        {
            var supplier = await _db.Suppliers
                .AsNoTracking()
                .Include(s => s.Products)
                .FirstOrDefaultAsync(s => s.Id == supplierId, ct);

            if (supplier == null) return Enumerable.Empty<SupplierProductResponse>();

            var productIds = supplier.Products.Select(p => p.Id).ToList();

            // Get last PO entry for these products from this supplier
            var lastPurchases = await _db.PurchaseOrderLines
                .AsNoTracking()
                .Include(l => l.PurchaseOrder)
                .Where(l => l.PurchaseOrder!.SupplierId == supplierId && productIds.Contains(l.ProductId))
                .GroupBy(l => l.ProductId)
                .Select(g => new
                {
                    ProductId = g.Key,
                    LastLine = g.OrderByDescending(l => l.PurchaseOrder!.CreatedUtc).FirstOrDefault()
                })
                .ToListAsync(ct); // Materialize first to simplify dictionary creation

            var historyMap = lastPurchases.ToDictionary(x => x.ProductId, x => x.LastLine);

            return supplier.Products.Select(p =>
            {
                var hasHistory = historyMap.TryGetValue(p.Id, out var last) && last != null;
                return new SupplierProductResponse(
                    p.Id,
                    p.Sku,
                    p.Name,
                    p.Unit,
                    hasHistory ? last!.UnitPrice : null,
                    hasHistory ? last!.BatchNumber : null,
                    hasHistory ? last!.PurchaseOrder!.CreatedUtc : null
                );
            });
        }

        public async Task UpdateSupplierProductsAsync(int supplierId, List<int> productIds, UserContext user, CancellationToken ct = default)
        {
            var supplier = await _db.Suppliers
                .Include(s => s.Products)
                .FirstOrDefaultAsync(s => s.Id == supplierId, ct);

            if (supplier == null) throw new NotFoundException($"Supplier {supplierId} not found.");

            // Get the products to be associated
            var productsToAdd = await _db.Products
                .Where(p => productIds.Contains(p.Id))
                .ToListAsync(ct);

            // Update collection
            // We want to keep the collection instance if possible, or just clear and add
            // For simple many-to-many in EF Core, clearing and re-adding works but deletes/inserts into join table.
            
            supplier.Products.Clear();
            foreach (var p in productsToAdd)
            {
                supplier.Products.Add(p);
            }

            await _db.SaveChangesAsync(ct);
            
            // Log update? 
            // Since it's a relation change, tracking it in generic AuditLog might be tricky without custom handling.
            // For now, we'll skip detailed relation auditing or log a generic update.
            await _auditWriter.LogUpdateAsync<Supplier>(supplierId, user, null, new { ProductCount = productsToAdd.Count }, ct);
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
