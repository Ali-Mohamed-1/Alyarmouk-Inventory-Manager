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
                b.Property(x => x.Address).HasMaxLength(500);
                b.HasIndex(x => x.Name);
            });

            builder.Entity<SalesOrder>(b =>
            {
                b.Property(x => x.OrderNumber).HasMaxLength(50).IsRequired();
                b.HasIndex(x => x.OrderNumber).IsUnique();
                b.Property(x => x.CustomerNameSnapshot).HasMaxLength(200).IsRequired();
                b.Property(x => x.CreatedByUserId).HasMaxLength(450).IsRequired();
                b.Property(x => x.CreatedByUserDisplayName).HasMaxLength(200).IsRequired();
                b.Property(x => x.Note).HasMaxLength(500);
                b.HasIndex(x => x.CreatedUtc);
                b.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<SalesOrderLine>(b =>
            {
                b.Property(x => x.ProductNameSnapshot).HasMaxLength(200).IsRequired();
                b.Property(x => x.UnitSnapshot).HasMaxLength(32).IsRequired();
                b.Property(x => x.Quantity).HasPrecision(18, 2);
                b.HasIndex(x => x.SalesOrderId);
                b.HasOne(x => x.SalesOrder).WithMany(o => o.Lines).HasForeignKey(x => x.SalesOrderId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
