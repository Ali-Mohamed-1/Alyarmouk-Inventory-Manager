using Inventory.Application.Abstractions;
using Inventory.Infrastructure.Data;
using Inventory.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfraStructure(this IServiceCollection services, IConfiguration config)
        {
            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseSqlServer(
                    config.GetConnectionString("DefaultConnection"),
                    sqlOptions =>
                    {
                        // Set command timeout to 300 seconds (5 minutes)
                        // This handles extremely slow localdb instances or complex transaction chains
                        sqlOptions.CommandTimeout(300);
                        
                        // Note: EnableRetryOnFailure is disabled because it conflicts with
                        // manual transaction management (BeginTransactionAsync).
                    });
            });

            // Application service registrations
            services.AddScoped<IProductServices, ProductServices>();
            services.AddScoped<ICustomerServices, CustomerServices>();
            services.AddScoped<ISalesOrderServices, SalesOrderServices>();
            services.AddScoped<IInventoryServices, InventoryServices>();
            services.AddScoped<IInventoryTransactionServices, InventoryTransactionServices>();
            services.AddScoped<IStockSnapshotServices, StockSnapshotServices>();
            services.AddScoped<IReportingServices, ReportingServices>();
            services.AddScoped<ISupplierServices, SupplierServices>();
            services.AddScoped<IPurchaseOrderServices, PurchaseOrderServices>();
            services.AddScoped<IProductBatchServices, ProductBatchServices>();
            services.AddScoped<IFinancialServices, FinancialServices>();
            services.AddScoped<INotificationService, NotificationService>();

            return services;
        }
    }
}