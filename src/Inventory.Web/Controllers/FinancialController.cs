using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs.Reporting;
using Inventory.Application.DTOs;

namespace Inventory.Web.Controllers
{
    [Authorize]
    public class FinancialController : Controller
    {
        private readonly IReportingServices _reportingServices;

        public FinancialController(IReportingServices reportingServices)
        {
            _reportingServices = reportingServices ?? throw new ArgumentNullException(nameof(reportingServices));
        }

        /// <summary>
        /// Main Financial Analysis view
        /// </summary>
        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// API endpoint to get financial summary for a selected period
        /// </summary>
        [HttpPost]
        [Route("api/financial/summary")]
        public async Task<IActionResult> GetFinancialSummary([FromBody] FinancialReportFilterDto filter, CancellationToken ct)
        {
            try
            {
                if (filter == null)
                    return BadRequest(new { error = "Filter is required" });

                // Validate custom date range
                if (filter.DateRangeType == FinancialDateRangeType.Custom)
                {
                    if (!filter.FromUtc.HasValue || !filter.ToUtc.HasValue)
                        return BadRequest(new { error = "Custom date range requires both FromUtc and ToUtc" });

                    if (filter.FromUtc.Value > filter.ToUtc.Value)
                        return BadRequest(new { error = "FromUtc must be before ToUtc" });
                }

                var summary = await _reportingServices.GetFinancialSummaryAsync(filter, ct);
                return Ok(summary);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// API endpoint to get internal expenses for a selected period
        /// </summary>
        [HttpPost]
        [Route("api/financial/expenses")]
        public async Task<IActionResult> GetInternalExpenses([FromBody] FinancialReportFilterDto filter, CancellationToken ct)
        {
            try
            {
                if (filter == null)
                    return BadRequest(new { error = "Filter is required" });

                var expenses = await _reportingServices.GetInternalExpensesAsync(filter, ct);
                return Ok(expenses);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// API endpoint to create a new internal expense
        /// </summary>
        [HttpPost]
        [Route("api/financial/expenses/create")]
        public async Task<IActionResult> CreateInternalExpense([FromBody] CreateInternalExpenseRequestDto request, CancellationToken ct)
        {
            try
            {
                if (request == null)
                    return BadRequest(new { error = "Request is required" });

                if (request.Amount <= 0)
                    return BadRequest(new { error = "Amount must be positive" });

                if (string.IsNullOrWhiteSpace(request.Description))
                    return BadRequest(new { error = "Description is required" });

                var user = GetUserContext();
                var expenseId = await _reportingServices.CreateInternalExpenseAsync(request, user, ct);

                return Ok(new { id = expenseId, message = "Expense created successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        private UserContext GetUserContext()
        {
            var userId = User?.Identity?.Name ?? "system";
            var displayName = User?.FindFirst("display_name")?.Value 
                ?? User?.Identity?.Name?.Split('@')[0] 
                ?? "System";
            return new UserContext(userId, displayName);
        }
    }
}
