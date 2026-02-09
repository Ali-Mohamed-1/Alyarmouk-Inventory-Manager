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
        public DbSet<Supplier> Suppliers { get; set; }
        public DbSet<Customer> Customers { get; set; }
        public DbSet<SalesOrder> SalesOrders { get; set; }
        public DbSet<SalesOrderLine> SalesOrderLines { get; set; }
        public DbSet<PurchaseOrder> PurchaseOrders { get; set; }
        public DbSet<PurchaseOrderLine> PurchaseOrderLines { get; set; }
        public DbSet<FinancialTransaction> FinancialTransactions { get; set; }
        public DbSet<ProductBatch> ProductBatches { get; set; }
        public DbSet<RefundTransaction> RefundTransactions { get; set; }
        public DbSet<RefundTransactionLine> RefundTransactionLines { get; set; }
        public DbSet<PaymentRecord> PaymentRecords { get; set; }
        public DbSet<BankSystemSettings> BankSystemSettings { get; set; }

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
                b.Property(x => x.UnitCost).HasPrecision(18, 2);
                b.Property(x => x.BatchNumber).HasMaxLength(100);
                b.Property(x => x.UserId).HasMaxLength(450).IsRequired();
                b.Property(x => x.UserDisplayName).HasMaxLength(200).IsRequired();
                b.Property(x => x.Note).HasMaxLength(500);
                b.HasIndex(x => x.TimestampUtc);
                b.HasIndex(x => x.ProductId);
                b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.ProductBatch).WithMany().HasForeignKey(x => x.ProductBatchId).OnDelete(DeleteBehavior.Restrict);
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
                b.Property(x => x.OrderDate).IsRequired();
                b.Property(x => x.DueDate).IsRequired();
                b.Property(x => x.InvoicePath).HasMaxLength(500);
                b.Property(x => x.ReceiptPath).HasMaxLength(500);
                b.Property(x => x.CreatedByUserId).HasMaxLength(450).IsRequired();
                b.Property(x => x.CreatedByUserDisplayName).HasMaxLength(200).IsRequired();
                b.Property(x => x.Note).HasMaxLength(500);
                b.Property(x => x.Status).IsRequired().HasConversion<int>();
                b.Property(x => x.PaymentMethod).IsRequired().HasConversion<int>();
                b.Property(x => x.PaymentStatus).IsRequired().HasConversion<int>();
                b.Property(x => x.Subtotal).HasPrecision(18, 2);
                b.Property(x => x.VatAmount).HasPrecision(18, 2);
                b.Property(x => x.ManufacturingTaxAmount).HasPrecision(18, 2);
                b.Property(x => x.TotalAmount).HasPrecision(18, 2);
                b.HasIndex(x => x.CreatedUtc);
                b.HasIndex(x => x.Status);
                b.HasIndex(x => x.PaymentStatus);
                b.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<SalesOrderLine>(b =>
            {
                b.Property(x => x.ProductNameSnapshot).HasMaxLength(200).IsRequired();
                b.Property(x => x.UnitSnapshot).HasMaxLength(32).IsRequired();
                b.Property(x => x.BatchNumber).HasMaxLength(100);
                b.Property(x => x.Quantity).HasPrecision(18, 2);
                b.Property(x => x.UnitPrice).HasPrecision(18, 2);
                b.Property(x => x.LineSubtotal).HasPrecision(18, 2);
                b.Property(x => x.LineVatAmount).HasPrecision(18, 2);
                b.Property(x => x.LineManufacturingTaxAmount).HasPrecision(18, 2);
                b.Property(x => x.LineTotal).HasPrecision(18, 2);
                b.HasIndex(x => x.SalesOrderId);
                b.HasOne(x => x.SalesOrder).WithMany(o => o.Lines).HasForeignKey(x => x.SalesOrderId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.ProductBatch).WithMany().HasForeignKey(x => x.ProductBatchId).OnDelete(DeleteBehavior.Restrict);
            });


            builder.Entity<Supplier>(b =>
            {
                b.Property(x => x.Name).HasMaxLength(200).IsRequired();
                b.Property(x => x.Phone).HasMaxLength(50);
                b.Property(x => x.Email).HasMaxLength(200);
                b.Property(x => x.Address).HasMaxLength(500);
                b.HasIndex(x => x.Name);

                b.HasMany(x => x.Products)
                 .WithMany(x => x.Suppliers)
                 .UsingEntity(j => j.ToTable("SupplierProducts"));
            });

            builder.Entity<PurchaseOrder>(b =>
            {
                b.HasKey(o => o.Id);
                b.Property(o => o.OrderNumber).IsRequired().HasMaxLength(50);
                b.Property(o => o.SupplierNameSnapshot).HasMaxLength(200);
                b.Property(o => o.TotalAmount).HasPrecision(18, 2);
                b.Property(o => o.Subtotal).HasPrecision(18, 2);
                b.Property(o => o.VatAmount).HasPrecision(18, 2);
                b.Property(o => o.ManufacturingTaxAmount).HasPrecision(18, 2);
                b.Property(o => o.ReceiptExpenses).HasPrecision(18, 2);
                b.Property(o => o.OrderDate).IsRequired();
                b.Property(o => o.RefundedAmount).HasPrecision(18, 2);

                b.HasOne(o => o.Supplier)
                 .WithMany()
                 .HasForeignKey(o => o.SupplierId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<PurchaseOrderLine>(b =>
            {
                b.Property(x => x.ProductNameSnapshot).HasMaxLength(200).IsRequired();
                b.Property(x => x.UnitSnapshot).HasMaxLength(32).IsRequired();
                b.Property(x => x.BatchNumber).HasMaxLength(100);
                b.Property(x => x.Quantity).HasPrecision(18, 2);
                b.Property(x => x.UnitPrice).HasPrecision(18, 2);
                b.Property(x => x.LineSubtotal).HasPrecision(18, 2);
                b.Property(x => x.LineVatAmount).HasPrecision(18, 2);
                b.Property(x => x.LineManufacturingTaxAmount).HasPrecision(18, 2);
                b.Property(x => x.LineTotal).HasPrecision(18, 2);
                b.HasIndex(x => x.PurchaseOrderId);
                b.HasOne(x => x.PurchaseOrder).WithMany(o => o.Lines).HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<FinancialTransaction>(b =>
            {
                b.Property(x => x.Amount).HasPrecision(18, 2);
                b.Property(x => x.UserId).HasMaxLength(450).IsRequired();
                b.Property(x => x.UserDisplayName).HasMaxLength(200).IsRequired();
                b.Property(x => x.Note).HasMaxLength(500);
                b.HasIndex(x => x.TimestampUtc);
                b.HasIndex(x => x.Type);
                b.HasIndex(x => x.ProductId);
                b.HasIndex(x => x.SalesOrderId);
                b.HasIndex(x => x.IsInternalExpense);
                b.HasOne(x => x.InventoryTransaction).WithMany().HasForeignKey(x => x.InventoryTransactionId).OnDelete(DeleteBehavior.SetNull);
                b.HasOne(x => x.SalesOrder).WithMany().HasForeignKey(x => x.SalesOrderId).OnDelete(DeleteBehavior.SetNull);
                b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
                b.HasIndex(x => x.SupplierId);
                b.HasIndex(x => x.PurchaseOrderId);
                b.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.PurchaseOrder).WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.SetNull);
            });

            builder.Entity<RefundTransaction>(b =>
            {
                b.Property(x => x.Amount).HasPrecision(18, 2);
                b.Property(x => x.Reason).HasMaxLength(500);
                b.Property(x => x.ProcessedByUserId).HasMaxLength(450).IsRequired();
                b.Property(x => x.ProcessedByUserDisplayName).HasMaxLength(200).IsRequired();
                b.Property(x => x.Note).HasMaxLength(1000);
                b.HasIndex(x => x.ProcessedUtc);
                b.HasIndex(x => x.SalesOrderId);
                b.HasIndex(x => x.PurchaseOrderId);
                b.HasOne(x => x.SalesOrder).WithMany().HasForeignKey(x => x.SalesOrderId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.PurchaseOrder).WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
                b.HasMany(x => x.Lines).WithOne(x => x.RefundTransaction).HasForeignKey(x => x.RefundTransactionId).OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<RefundTransactionLine>(b =>
            {
                b.Property(x => x.Quantity).HasPrecision(18, 2);
                b.Property(x => x.UnitPriceSnapshot).HasPrecision(18, 2);
                b.Property(x => x.LineRefundAmount).HasPrecision(18, 2);
                b.Property(x => x.ProductNameSnapshot).HasMaxLength(200).IsRequired();
                b.Property(x => x.BatchNumber).HasMaxLength(100);
                b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.SalesOrderLine).WithMany().HasForeignKey(x => x.SalesOrderLineId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.PurchaseOrderLine).WithMany().HasForeignKey(x => x.PurchaseOrderLineId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.ProductBatch).WithMany().HasForeignKey(x => x.ProductBatchId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<ProductBatch>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.BatchNumber).HasMaxLength(100).IsRequired();
                b.Property(x => x.UnitCost).HasPrecision(18, 2);
                b.Property(x => x.UnitPrice).HasPrecision(18, 2);
                b.Property(x => x.OnHand).HasPrecision(18, 2);
                b.Property(x => x.Reserved).HasPrecision(18, 2);
                b.Property(x => x.Notes).HasMaxLength(500);
                b.Property(x => x.RowVersion).IsRowVersion();
                b.HasIndex(x => new { x.ProductId, x.BatchNumber }).IsUnique();
                b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<PaymentRecord>(b =>
            {
                b.Property(x => x.Amount).HasPrecision(18, 2);
                b.Property(x => x.Reference).HasMaxLength(100);
                b.Property(x => x.Note).HasMaxLength(500);
                b.Property(x => x.CreatedByUserId).HasMaxLength(450);
                b.HasIndex(x => x.SalesOrderId);
                b.HasIndex(x => x.PurchaseOrderId);
                
                b.HasOne(x => x.SalesOrder)
                 .WithMany(o => o.Payments)
                 .HasForeignKey(x => x.SalesOrderId)
                 .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(x => x.PurchaseOrder)
                 .WithMany(o => o.Payments)
                 .HasForeignKey(x => x.PurchaseOrderId)
                 .OnDelete(DeleteBehavior.Cascade);
            });
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            base.OnConfiguring(optionsBuilder);
            optionsBuilder.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
        }
    }
}
