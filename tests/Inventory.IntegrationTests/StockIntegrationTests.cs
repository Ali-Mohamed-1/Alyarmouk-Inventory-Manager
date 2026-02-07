using System;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.Transaction;
using Inventory.Domain.Entities;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventory.IntegrationTests;

public class StockIntegrationTests : IClassFixture<IntegrationTestFixture>, IDisposable
{
    private readonly IntegrationTestFixture _fixture;
    private readonly IServiceScope _scope;
    private readonly AppDbContext _db;
    private readonly IInventoryServices _inventoryServices;
    private readonly IStockSnapshotServices _stockSnapshotServices;
    private readonly UserContext _user;

    public StockIntegrationTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _scope = _fixture.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _inventoryServices = _scope.ServiceProvider.GetRequiredService<IInventoryServices>();
        _stockSnapshotServices = _scope.ServiceProvider.GetRequiredService<IStockSnapshotServices>();
        _user = new UserContext("test-user", "Test User");
        _db.Database.EnsureCreated();
        TestDataSeeder.SeedAsync(_db).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _scope.Dispose();
    }

    [Fact]
    public async Task IssueStock_DoesNotAffectMoney()
    {
        var ct = CancellationToken.None;
        var beforeTxCount = await _db.FinancialTransactions.CountAsync(ct);

        await _inventoryServices.IssueAsync(new StockIssueRequest
        {
            ProductId = 1,
            Quantity = 5,
            BatchNumber = "BATCH-001",
            Note = "Test issue"
        }, _user, ct);

        var afterTxCount = await _db.FinancialTransactions.CountAsync(ct);
        Assert.Equal(beforeTxCount, afterTxCount);
    }

    [Fact]
    public async Task AdjustStock_DoesNotAffectMoney()
    {
        var ct = CancellationToken.None;
        var beforeTxCount = await _db.FinancialTransactions.CountAsync(ct);

        await _inventoryServices.UpdateStockAsync(new Inventory.Application.DTOs.StockSnapshot.UpdateStockRequest
        {
            ProductId = 1,
            Adjustment = 10,
            Note = "Test adjust",
            RowVersion = "AAAAAAAAAAA="
        }, _user, ct);

        var afterTxCount = await _db.FinancialTransactions.CountAsync(ct);
        Assert.Equal(beforeTxCount, afterTxCount);
    }

    [Fact]
    public async Task ProductOnHand_EqualsBatchAggregates()
    {
        var ct = CancellationToken.None;
        var stock = await _stockSnapshotServices.GetByProductIdAsync(1, ct);
        Assert.NotNull(stock);

        var batchSum = await _db.ProductBatches
            .Where(b => b.ProductId == 1)
            .GroupBy(b => b.ProductId)
            .Select(g => new { OnHand = g.Sum(b => b.OnHand), Reserved = g.Sum(b => b.Reserved) })
            .FirstAsync(ct);

        Assert.Equal(batchSum.OnHand, stock.OnHand);
        Assert.Equal(batchSum.Reserved, stock.Reserved);
    }
}
