using Inventory.Application.DTOs.Reporting;
using Inventory.Application.DTOs.PurchaseOrder;
using Inventory.Application.DTOs.SalesOrder;
using Inventory.Application.DTOs;

namespace Inventory.Application.Abstractions
{
    public interface IReportingServices
    {
        /// <summary>
        /// Gathers the key dashboard metrics
        /// </summary>
        Task<DashboardResponseDto> GetDashboardAsync(CancellationToken ct = default);

        /// <summary>
        /// Returns items that are running low so teams can restock early
        /// </summary>
        Task<IReadOnlyList<LowStockItemResponseDto>> GetLowStockAsync(CancellationToken ct = default);

        /// <summary>
        /// Returns aggregated order/payment totals and balance for a supplier.
        /// </summary>
        Task<SupplierBalanceResponseDto> GetSupplierBalanceAsync(int supplierId, CancellationToken ct = default);

        /// <summary>
        /// Returns how much a customer owes, leveraging payment due dates.
        /// TotalPending = all unpaid orders, TotalDueNow = unpaid orders with DueDate &lt;= AsOfUtc.
        /// </summary>
        Task<CustomerBalanceResponseDto> GetCustomerBalanceAsync(int customerId, DateTimeOffset? asOfUtc = null, CancellationToken ct = default);

        /// <summary>
        /// Returns a high-level financial summary for the given period.
        /// Used by the Financial Reports tab.
        /// </summary>
        Task<FinancialSummaryResponseDto> GetFinancialSummaryAsync(FinancialReportFilterDto filter, CancellationToken ct = default);

        /// <summary>
        /// Returns all internal expenses within the specified period.
        /// This includes explicitly created internal expenses and purchase receipt expenses.
        /// </summary>
        Task<IReadOnlyList<InternalExpenseResponseDto>> GetInternalExpensesAsync(FinancialReportFilterDto filter, CancellationToken ct = default);

        /// <summary>
        /// Records a new internal expense as a FinancialTransaction.
        /// </summary>
        Task<long> CreateInternalExpenseAsync(CreateInternalExpenseRequestDto request, UserContext user, CancellationToken ct = default);
    }
}
