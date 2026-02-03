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

        public async Task ProcessSalesPaymentAsync(long salesOrderId, UserContext user, CancellationToken ct = default)
        {
            var order = await _db.SalesOrders
                .FirstOrDefaultAsync(o => o.Id == salesOrderId, ct);
            if (order == null) throw new NotFoundException($"Sales order {salesOrderId} not found.");

            if (order.PaymentStatus != PaymentStatus.Paid)
            {
                order.PaymentStatus = PaymentStatus.Paid;
            }

            // Create Revenue Financial Transaction
            decimal paymentAmount = order.TotalAmount - order.RefundedAmount;
            if (paymentAmount > 0)
            {
                var revenueTx = new FinancialTransaction
                {
                    Type = FinancialTransactionType.Revenue,
                    Amount = paymentAmount,
                    SalesOrderId = order.Id,
                    CustomerId = order.CustomerId,
                    TimestampUtc = DateTimeOffset.UtcNow,
                    UserId = user.UserId,
                    UserDisplayName = user.UserDisplayName,
                    Note = $"Payment received for Sales Order {order.OrderNumber}"
                };
                _db.FinancialTransactions.Add(revenueTx);
            }

            await _db.SaveChangesAsync(ct);
        }

        public async Task ProcessPurchasePaymentAsync(long purchaseOrderId, UserContext user, CancellationToken ct = default)
        {
            var order = await _db.PurchaseOrders
                .FirstOrDefaultAsync(o => o.Id == purchaseOrderId, ct);
            if (order == null) throw new NotFoundException($"Purchase order {purchaseOrderId} not found.");

            if (order.PaymentStatus != PurchasePaymentStatus.Paid)
            {
                order.PaymentStatus = PurchasePaymentStatus.Paid;
            }

            // Create Expense Financial Transaction
            decimal paymentAmount = order.TotalAmount - order.RefundedAmount;
            if (paymentAmount > 0)
            {
                var expenseTx = new FinancialTransaction
                {
                    Type = FinancialTransactionType.Expense,
                    Amount = paymentAmount,
                    PurchaseOrderId = order.Id,
                    SupplierId = order.SupplierId,
                    TimestampUtc = DateTimeOffset.UtcNow,
                    UserId = user.UserId,
                    UserDisplayName = user.UserDisplayName,
                    Note = $"Payment made for Purchase Order {order.OrderNumber}"
                };
                _db.FinancialTransactions.Add(expenseTx);
            }

            await _db.SaveChangesAsync(ct);
        }

        public async Task ReverseSalesPaymentAsync(long salesOrderId, UserContext user, CancellationToken ct = default)
        {
            var order = await _db.SalesOrders
                .FirstOrDefaultAsync(o => o.Id == salesOrderId, ct);
            if (order == null) throw new NotFoundException($"Sales order {salesOrderId} not found.");

            // Instead of deleting, create reversal transactions
            decimal paymentAmount = order.TotalAmount - order.RefundedAmount;
            if (paymentAmount > 0)
            {
                // Reverse Revenue by creating Expense
                var reversalTx = new FinancialTransaction
                {
                    Type = FinancialTransactionType.Expense,
                    Amount = paymentAmount,
                    SalesOrderId = order.Id,
                    CustomerId = order.CustomerId,
                    TimestampUtc = DateTimeOffset.UtcNow,
                    UserId = user.UserId,
                    UserDisplayName = user.UserDisplayName,
                    Note = $"Payment Reversal for Sales Order {order.OrderNumber}"
                };
                _db.FinancialTransactions.Add(reversalTx);
            }

            order.PaymentStatus = PaymentStatus.Pending;
            await _db.SaveChangesAsync(ct);
        }

        public async Task ReversePurchasePaymentAsync(long purchaseOrderId, UserContext user, CancellationToken ct = default)
        {
            var order = await _db.PurchaseOrders
                .FirstOrDefaultAsync(o => o.Id == purchaseOrderId, ct);
            if (order == null) throw new NotFoundException($"Purchase order {purchaseOrderId} not found.");

            // Instead of deleting, create reversal transactions
            decimal paymentAmount = order.TotalAmount - order.RefundedAmount;
            if (paymentAmount > 0)
            {
                // Reverse Expense by creating Revenue
                var reversalTx = new FinancialTransaction
                {
                    Type = FinancialTransactionType.Revenue,
                    Amount = paymentAmount,
                    PurchaseOrderId = order.Id,
                    SupplierId = order.SupplierId,
                    TimestampUtc = DateTimeOffset.UtcNow,
                    UserId = user.UserId,
                    UserDisplayName = user.UserDisplayName,
                    Note = $"Payment Reversal for Purchase Order {order.OrderNumber}"
                };
                _db.FinancialTransactions.Add(reversalTx);
            }

            order.PaymentStatus = PurchasePaymentStatus.Unpaid;
            await _db.SaveChangesAsync(ct);
        }

        public async Task ProcessSalesRefundPaymentAsync(long salesOrderId, decimal amount, UserContext user, CancellationToken ct = default)
        {
            var order = await _db.SalesOrders.FirstOrDefaultAsync(o => o.Id == salesOrderId, ct);
            if (order == null) throw new NotFoundException($"Sales order {salesOrderId} not found.");

            // Sales Refund = We pay money BACK to customer. This is an EXPENSE.
            // Or 'Revenue Reversal'? Usually treated as Expense or Contra-Revenue.
            // System uses Expense/Revenue types. Refund to customer reduces our cash, so Expense.
            
            var refundTx = new FinancialTransaction
            {
                Type = FinancialTransactionType.Expense,
                Amount = amount,
                SalesOrderId = order.Id,
                CustomerId = order.CustomerId,
                TimestampUtc = DateTimeOffset.UtcNow,
                UserId = user.UserId,
                UserDisplayName = user.UserDisplayName,
                Note = $"Refund for Sales Order {order.OrderNumber}"
            };
            
            _db.FinancialTransactions.Add(refundTx);
            await _db.SaveChangesAsync(ct);
        }

        public async Task ProcessPurchaseRefundPaymentAsync(long purchaseOrderId, decimal amount, UserContext user, CancellationToken ct = default)
        {
            var order = await _db.PurchaseOrders.FirstOrDefaultAsync(o => o.Id == purchaseOrderId, ct);
            if (order == null) throw new NotFoundException($"Purchase order {purchaseOrderId} not found.");

            // Purchase Refund = Supplier pays money BACK to us. This is REVENUE (or Contra-Expense).
            // Refund from supplier increases our cash, so Revenue.

            var refundTx = new FinancialTransaction
            {
                Type = FinancialTransactionType.Revenue,
                Amount = amount,
                PurchaseOrderId = order.Id,
                SupplierId = order.SupplierId,
                TimestampUtc = DateTimeOffset.UtcNow,
                UserId = user.UserId,
                UserDisplayName = user.UserDisplayName,
                Note = $"Refund for Purchase Order {order.OrderNumber}"
            };

            _db.FinancialTransactions.Add(refundTx);
            await _db.SaveChangesAsync(ct);
        }
    }
}
