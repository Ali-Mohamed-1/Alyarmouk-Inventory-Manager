using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Domain.Entities;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Services
{
    public sealed class FinancialServices : IFinancialServices
    {
        private readonly AppDbContext _db;

        public FinancialServices(AppDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task CreateFinancialTransactionFromPaymentAsync(PaymentRecord payment, UserContext user, CancellationToken ct = default)
        {
            if (payment == null) throw new ArgumentNullException(nameof(payment));
            if (user == null) throw new ArgumentNullException(nameof(user));

            FinancialTransactionType txType;
            string notePrefix;

            if (payment.OrderType == OrderType.SalesOrder)
            {
                txType = payment.PaymentType == PaymentRecordType.Payment 
                    ? FinancialTransactionType.Revenue 
                    : FinancialTransactionType.Expense;
                notePrefix = payment.PaymentType == PaymentRecordType.Payment ? "Payment received" : "Refund processed";
            }
            else // PurchaseOrder
            {
                txType = payment.PaymentType == PaymentRecordType.Payment 
                    ? FinancialTransactionType.Expense 
                    : FinancialTransactionType.Revenue;
                notePrefix = payment.PaymentType == PaymentRecordType.Payment ? "Payment made" : "Refund received";
            }

            var tx = new FinancialTransaction
            {
                Type = txType,
                Amount = payment.Amount,
                TimestampUtc = payment.PaymentDate,
                UserId = user.UserId,
                UserDisplayName = user.UserDisplayName,
                PaymentRecord = payment,
                Note = $"{notePrefix} for {payment.OrderType} (Ref: {payment.Reference}). {payment.Note}".Trim()
            };

            if (payment.OrderType == OrderType.SalesOrder)
            {
                tx.SalesOrderId = payment.SalesOrderId;
                // If CustomerId isn't on payment, we might need to fetch it from the order
                if (payment.SalesOrder != null)
                {
                    tx.CustomerId = payment.SalesOrder.CustomerId;
                }
                else if (payment.SalesOrderId.HasValue)
                {
                    var order = await _db.SalesOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == payment.SalesOrderId, ct);
                    tx.CustomerId = order?.CustomerId;
                }
            }
            else
            {
                tx.PurchaseOrderId = payment.PurchaseOrderId;
                if (payment.PurchaseOrder != null)
                {
                    tx.SupplierId = payment.PurchaseOrder.SupplierId;
                }
                else if (payment.PurchaseOrderId.HasValue)
                {
                    var order = await _db.PurchaseOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == payment.PurchaseOrderId, ct);
                    tx.SupplierId = order?.SupplierId;
                }
            }

            _db.FinancialTransactions.Add(tx);
            // We don't call SaveChangesAsync here because it should be called by the caller as part of the transaction.
        }
    }
}
