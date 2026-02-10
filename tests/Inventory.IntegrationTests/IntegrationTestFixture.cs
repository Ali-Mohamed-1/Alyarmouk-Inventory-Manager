using Inventory.Application.Abstractions;
using Inventory.Infrastructure.Data;
using Inventory.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.IntegrationTests;

/// <summary>
/// Builds a service provider with real services and an in-memory database for integration testing.
/// No domain logic is mocked—all business rules execute end-to-end.
/// </summary>
public sealed class IntegrationTestFixture : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private bool _disposed;

    public IntegrationTestFixture()
    {
        var services = new ServiceCollection();

        var dbName = Guid.NewGuid().ToString();
        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
        });

        // Register all real services (mirrors DependencyInjection.AddInfraStructure)
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

        _serviceProvider = services.BuildServiceProvider();
    }

    public IServiceScope CreateScope() => _serviceProvider.CreateScope();

    public void Dispose()
    {
        if (_disposed) return;
        if (_serviceProvider is IDisposable d)
            d.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
