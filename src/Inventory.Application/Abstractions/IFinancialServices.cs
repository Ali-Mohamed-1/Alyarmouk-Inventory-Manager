using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.DTOs;
using Inventory.Domain.Entities;

namespace Inventory.Application.Abstractions
{
    public interface IFinancialServices
    {
        /// <summary>
        /// Creates a matching FinancialTransaction for a given PaymentRecord.
        /// This is the MANDATORY way to record money movement in the financial log.
        /// Must be called within the same database transaction as the PaymentRecord creation.
        /// </summary>
        Task CreateFinancialTransactionFromPaymentAsync(PaymentRecord payment, UserContext user, CancellationToken ct = default);

        // Legacy methods for status-based automation - these should be refactored to use PaymentRecord internally if kept,
        // but for now I will mark them or remove if not needed.
        // Actually, the new rules say NO money movement without PaymentRecord.
        // So these MUST be removed or updated to take a PaymentRecord.
    }
}
