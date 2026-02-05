using System;
using System.Collections.Generic;
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
    public class NotificationService : INotificationService
    {
        private readonly AppDbContext _db;

        public NotificationService(AppDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<IEnumerable<PaymentNotificationDto>> GetActiveNotificationsAsync(CancellationToken ct = default)
        {
            var now = DateTimeOffset.UtcNow;
            var upcomingWindow = now.AddDays(7);

            var notifications = new List<PaymentNotificationDto>();

            // 1. Sales Orders Notifications
            // Criteria:
            // - Not Cancelled
            // - RemainingAmount > 0 (money still owed)
            // - DueDate within next 7 days and not in the past
            var salesOrders = await _db.SalesOrders
                .AsNoTracking()
                .Include(o => o.Payments)
                .Where(o => o.Status != SalesOrderStatus.Cancelled &&
                            o.DueDate >= now &&
                            o.DueDate <= upcomingWindow)
                .ToListAsync(ct);

            foreach (var so in salesOrders)
            {
                var paid = so.Payments
                    .Where(p => p.PaymentType == PaymentRecordType.Payment)
                    .Sum(p => p.Amount);

                var refunded = so.Payments
                    .Where(p => p.PaymentType == PaymentRecordType.Refund)
                    .Sum(p => p.Amount);

                var netPaid = paid - refunded;
                var remainingAmount = so.TotalAmount - netPaid;

                if (remainingAmount <= 0)
                {
                    continue;
                }

                var daysUntil = (so.DueDate - now).Days;
                // Ensure at least 0 if it's due today but technically "future" by time
                if (daysUntil < 0) daysUntil = 0; 
                
                notifications.Add(new PaymentNotificationDto
                {
                    NotificationId = $"SO-{so.Id}",
                    OrderId = so.Id,
                    OrderNumber = so.OrderNumber,
                    Type = "Sales",
                    CounterpartyId = so.CustomerId,
                    CounterpartyName = so.CustomerNameSnapshot,
                    PaymentDeadline = so.DueDate,
                    RemainingAmount = remainingAmount,
                    DaysUntilDue = daysUntil,
                    Message = $"Payment due in {daysUntil} days for Sales Order #{so.OrderNumber} from {so.CustomerNameSnapshot}"
                });
            }

            // 2. Purchase Orders Notifications
            // Criteria:
            // - Not Cancelled
            // - RemainingAmount > 0 (money still owed)
            // - PaymentDeadline exists and is within the next 7 days
            var purchaseOrders = await _db.PurchaseOrders
                .AsNoTracking()
                .Include(o => o.Payments)
                .Where(o => o.Status != PurchaseOrderStatus.Cancelled &&
                            o.PaymentDeadline.HasValue &&
                            o.PaymentDeadline.Value >= now &&
                            o.PaymentDeadline.Value <= upcomingWindow)
                .ToListAsync(ct);

            foreach (var po in purchaseOrders)
            {
                var paid = po.Payments
                    .Where(p => p.PaymentType == PaymentRecordType.Payment)
                    .Sum(p => p.Amount);

                var refunded = po.Payments
                    .Where(p => p.PaymentType == PaymentRecordType.Refund)
                    .Sum(p => p.Amount);

                var netPaid = paid - refunded;
                var remainingAmount = po.TotalAmount - netPaid;

                if (remainingAmount <= 0)
                {
                    continue;
                }

                var paymentDeadline = po.PaymentDeadline!.Value;
                var daysUntil = (paymentDeadline - now).Days;
                 if (daysUntil < 0) daysUntil = 0;

                notifications.Add(new PaymentNotificationDto
                {
                    NotificationId = $"PO-{po.Id}",
                    OrderId = po.Id,
                    OrderNumber = po.OrderNumber,
                    Type = "Purchase",
                    CounterpartyId = po.SupplierId,
                    CounterpartyName = po.SupplierNameSnapshot,
                    PaymentDeadline = paymentDeadline,
                    RemainingAmount = remainingAmount,
                    DaysUntilDue = daysUntil,
                    Message = $"Payment due in {daysUntil} days for Purchase Order #{po.OrderNumber} to {po.SupplierNameSnapshot}"
                });
            }

            return notifications.OrderBy(n => n.PaymentDeadline);
        }
    }
}
