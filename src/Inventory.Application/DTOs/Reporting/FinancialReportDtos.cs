using System;
using System.Collections.Generic;
using Inventory.Domain.Entities;

namespace Inventory.Application.DTOs.Reporting
{
    public enum FinancialDateRangeType
    {
        Today = 0,
        ThisWeek = 1,
        ThisMonth = 2,
        ThisQuarter = 3,
        ThisYear = 4,
        Custom = 5
    }

    /// <summary>
    /// Filter options for financial reports tab.
    /// </summary>
    public sealed class FinancialReportFilterDto
    {
        public FinancialDateRangeType DateRangeType { get; set; } = FinancialDateRangeType.Today;

        /// <summary>
        /// Optional explicit date range (UTC). Used when DateRangeType is Custom.
        /// </summary>
        public DateTimeOffset? FromUtc { get; set; }
        public DateTimeOffset? ToUtc { get; set; }
    }

    /// <summary>
    /// High-level financial metrics for the selected period.
    /// </summary>
    public sealed class FinancialSummaryResponseDto
    {
        public decimal TotalSales { get; set; }
        public decimal CostOfGoods { get; set; }

        /// <summary>
        /// Sum(Sales VAT) - Sum(Purchase VAT) for the selected range.
        /// </summary>
        public decimal TotalVat { get; set; }

        /// <summary>
        /// Sum(Sales Manufacturing Tax) - Sum(Purchase Manufacturing Tax) for the selected range.
        /// </summary>
        public decimal TotalManufacturingTax { get; set; }

        /// <summary>
        /// Internal expenses such as rent, salaries, plus purchase receipt expenses.
        /// </summary>
        public decimal InternalExpenses { get; set; }

        public decimal GrossProfit { get; set; }
        public decimal NetProfit { get; set; }
    }

    /// <summary>
    /// DTO used when creating a new internal expense from the UI.
    /// </summary>
    public sealed class CreateInternalExpenseRequestDto
    {
        public InternalExpenseType InternalExpenseType { get; set; }

        /// <summary>
        /// Short description visible in the financial reports tab.
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Optional detailed note.
        /// </summary>
        public string? Note { get; set; }

        public decimal Amount { get; set; }

        /// <summary>
        /// When the expense should be considered effective. Defaults to now if not supplied.
        /// </summary>
        public DateTimeOffset? TimestampUtc { get; set; }
    }

    public sealed class InternalExpenseResponseDto
    {
        public long Id { get; set; }
        public InternalExpenseType InternalExpenseType { get; set; }
        public string Description { get; set; } = string.Empty;
        public string? Note { get; set; }
        public decimal Amount { get; set; }
        public DateTimeOffset TimestampUtc { get; set; }
        public string CreatedByUserDisplayName { get; set; } = string.Empty;
    }
}

