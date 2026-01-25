using Inventory.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Data
{
    public class AppDbContext : IdentityDbContext<AppUser, IdentityRole, string>
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<Product> Products { get; set; }
        public DbSet<Category> categories { get; set; }
        public DbSet<InventoryTransaction> InventoryTransactions { get; set; }
        public DbSet<StockSnapshot> StockSnapshots { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<Customer> Customers { get; set; }
        public DbSet<SalesOrder> SalesOrders { get; set; }
        public DbSet<SalesOrderLine> SalesOrderLines { get; set; }
        public DbSet<PurchaseOrder> PurchaseOrders { get; set; }
        public DbSet<PurchaseOrderLine> PurchaseOrderLines { get; set; }
        public DbSet<Supplier> Suppliers { get; set; }
        public DbSet<InternalExpense> InternalExpenses { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<Category>(c =>
            {
                c.Property(c => c.Name).IsRequired().HasMaxLength(200);
            });

            builder.Entity<Product>(b =>
            {
                b.Property(x => x.Sku).HasMaxLength(64).IsRequired();
                b.HasIndex(x => x.Sku).IsUnique();
                b.Property(x => x.Name).HasMaxLength(200).IsRequired();
                b.Property(x => x.Unit).HasMaxLength(32).IsRequired();
                b.Property(x => x.ReorderPoint).HasPrecision(18, 2);
                b.Property(x => x.RowVersion).IsRowVersion();
                b.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<StockSnapshot>(b =>
            {
                b.HasKey(x => x.ProductId);
                b.Property(x => x.OnHand).HasPrecision(18, 2);
                b.Property(x => x.Preserved).HasPrecision(18, 2);
                b.Property(x => x.RowVersion).IsRowVersion();
                b.HasOne(x => x.Product).WithOne().HasForeignKey<StockSnapshot>(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<InventoryTransaction>(b =>
            {
                b.Property(x => x.QuantityDelta).HasPrecision(18, 2);
                b.Property(x => x.UserId).HasMaxLength(450).IsRequired();
                b.Property(x => x.UserDisplayName).HasMaxLength(200).IsRequired();
                b.Property(x => x.Note).HasMaxLength(500);
                b.HasIndex(x => x.TimestampUtc);
                b.HasIndex(x => x.ProductId);
                b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<AuditLog>(b =>
            {
                b.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
                b.Property(x => x.EntityId).HasMaxLength(100).IsRequired();
                b.Property(x => x.UserId).HasMaxLength(450).IsRequired();
                b.Property(x => x.UserDisplayName).HasMaxLength(200).IsRequired();
                b.Property(x => x.ChangesJson).HasMaxLength(4000);
                b.HasIndex(x => x.TimestampUtc);
                b.HasIndex(x => new { x.EntityType, x.EntityId });
            });

            builder.Entity<Customer>(b =>
            {
                b.Property(x => x.Name).HasMaxLength(200).IsRequired();
                b.Property(x => x.Phone).HasMaxLength(50);
                b.Property(x => x.Email).HasMaxLength(200);
                b.HasIndex(x => x.Name);
            });

            builder.Entity<SalesOrder>(b =>
            {
                b.ToTable("SalesOrders");
                b.HasKey(x => x.Id);

                // Standard Fields
                b.Property(x => x.OrderNumber).HasMaxLength(50).IsRequired();
                b.HasIndex(x => x.OrderNumber).IsUnique();

                b.Property(x => x.CustomerNameSnapshot).HasMaxLength(200).IsRequired();
                b.Property(x => x.CreatedByUserId).HasMaxLength(450).IsRequired();
                b.Property(x => x.CreatedByUserDisplayName).HasMaxLength(200).IsRequired();
                b.Property(x => x.Note).HasMaxLength(500);
                b.HasIndex(x => x.CreatedUtc);

                b.Property(x => x.Subtotal).HasPrecision(18, 2);
                b.Property(x => x.VatAmount).HasPrecision(18, 2);
                b.Property(x => x.ManufacturingTaxAmount).HasPrecision(18, 2);
                b.Property(x => x.TotalAmount).HasPrecision(18, 2);

                // Relationships
                b.HasOne(x => x.Customer)
                    .WithMany()
                    .HasForeignKey(x => x.CustomerId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<SalesOrderLine>(b =>
            {
                b.ToTable("SalesOrderLines");
                b.HasKey(x => x.Id);

                // Standard Fields
                b.Property(x => x.ProductNameSnapshot).HasMaxLength(200).IsRequired();
                b.Property(x => x.UnitSnapshot).HasMaxLength(32).IsRequired();

                // Quantity often needs 3 decimals (e.g. 1.500 Liters)
                b.Property(x => x.Quantity).HasPrecision(18, 3);

                b.Property(x => x.UnitPrice).HasPrecision(18, 2);
                b.Property(x => x.LineSubtotal).HasPrecision(18, 2);
                b.Property(x => x.LineVatAmount).HasPrecision(18, 2);
                b.Property(x => x.LineManufacturingTaxAmount).HasPrecision(18, 2);
                b.Property(x => x.LineTotal).HasPrecision(18, 2);

                // Relationships
                b.HasIndex(x => x.SalesOrderId);

                b.HasOne(x => x.SalesOrder)
                    .WithMany(o => o.Lines)
                    .HasForeignKey(x => x.SalesOrderId)
                    .OnDelete(DeleteBehavior.Cascade); // Deleting Order deletes Lines

                b.HasOne(x => x.Product)
                    .WithMany()
                    .HasForeignKey(x => x.ProductId)
                    .OnDelete(DeleteBehavior.Restrict); // Cannot delete Product if sold
            });

            builder.Entity<InternalExpense>(b =>
            {
                b.ToTable("InternalExpenses");
                b.HasKey(e => e.Id);

                b.Property(e => e.ExpenseType).IsRequired().HasMaxLength(50);
                b.Property(e => e.Description).HasMaxLength(500);
                b.Property(e => e.Amount).HasPrecision(18, 2).IsRequired();

                b.Property(e => e.CreatedByUserDisplayName).HasMaxLength(100);
                b.Property(e => e.CreatedByUserId).HasMaxLength(450);
                b.Property(e => e.Note).HasMaxLength(1000);

                b.HasIndex(e => e.ExpenseDate); // Optimization for date-range queries
            });

            builder.Entity<Supplier>(b =>
            {
                b.ToTable("Suppliers");
                b.HasKey(s => s.Id);

                b.Property(s => s.Name).IsRequired().HasMaxLength(200);
                b.Property(s => s.Phone).HasMaxLength(20);
                b.Property(s => s.Email).HasMaxLength(100);
                b.Property(s => s.Address).HasMaxLength(500);

                b.HasIndex(s => s.Name).IsUnique(); // Prevent duplicate suppliers
            });

            builder.Entity<PurchaseOrder>(b =>
            {
                b.ToTable("PurchaseOrders");
                b.HasKey(p => p.Id);

                b.Property(p => p.OrderNumber).IsRequired().HasMaxLength(50);
                b.HasIndex(p => p.OrderNumber).IsUnique();

                b.Property(p => p.SupplierNameSnapshot).IsRequired().HasMaxLength(200);
                b.Property(p => p.Note).HasMaxLength(2000);

                // Money Fields
                b.Property(p => p.Subtotal).HasPrecision(18, 2);
                b.Property(p => p.VatAmount).HasPrecision(18, 2);
                b.Property(p => p.ManufacturingTaxAmount).HasPrecision(18, 2);
                b.Property(p => p.ReceiptExpenses).HasPrecision(18, 2);
                b.Property(p => p.TotalAmount).HasPrecision(18, 2);

                // Relationships
                b.HasOne(p => p.Supplier)
                 .WithMany()
                 .HasForeignKey(p => p.SupplierId)
                 .OnDelete(DeleteBehavior.Restrict); // Don't delete supplier if they have orders
            });

            builder.Entity<PurchaseOrderLine>(b =>
            {
                b.ToTable("PurchaseOrderLines");
                b.HasKey(l => l.Id);

                b.Property(l => l.ProductNameSnapshot).IsRequired().HasMaxLength(200);
                b.Property(l => l.UnitSnapshot).HasMaxLength(50);

                // Quantity often needs 3 decimal places (e.g. 1.500 kg)
                b.Property(l => l.Quantity).HasPrecision(18, 3);

                // Money Fields
                b.Property(l => l.UnitPrice).HasPrecision(18, 2);
                b.Property(l => l.LineSubtotal).HasPrecision(18, 2);
                b.Property(l => l.LineVatAmount).HasPrecision(18, 2);
                b.Property(l => l.LineManufacturingTaxAmount).HasPrecision(18, 2);
                b.Property(l => l.LineTotal).HasPrecision(18, 2);

                // Relationships
                b.HasOne(l => l.PurchaseOrder)
                 .WithMany(p => p.Lines)
                 .HasForeignKey(l => l.PurchaseOrderId)
                 .OnDelete(DeleteBehavior.Cascade); // If PO is deleted, delete its lines

                b.HasOne(l => l.Product)
                 .WithMany()
                 .HasForeignKey(l => l.ProductId)
                 .OnDelete(DeleteBehavior.Restrict); // Don't delete product if it's on a PO
            });
        }
    }
}
