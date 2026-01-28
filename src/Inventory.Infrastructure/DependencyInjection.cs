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
                options.UseSqlServer(config.GetConnectionString("DefaultConnection"));
            });

            // Application service registrations
            services.AddScoped<IProductServices, ProductServices>();
            services.AddScoped<ICategoryServices, CategoryServices>();
            services.AddScoped<ICustomerServices, CustomerServices>();
            services.AddScoped<ISalesOrderServices, SalesOrderServices>();
            services.AddScoped<IInventoryServices, InventoryServices>();
            services.AddScoped<IInventoryTransactionServices, InventoryTransactionServices>();
            services.AddScoped<IStockSnapshotServices, StockSnapshotServices>();
            services.AddScoped<IReportingServices, ReportingServices>();
            services.AddScoped<IAuditLogServices, AuditLogServices>();
            services.AddScoped<IAuditLogWriter, AuditLogWriter>();
            services.AddScoped<ISupplierServices, SupplierServices>();
            services.AddScoped<IPurchaseOrderServices, PurchaseOrderServices>();

            return services;
        }
    }
}
