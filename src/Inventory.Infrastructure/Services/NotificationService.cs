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
            // Criteria: Not Paid, DueDate within next 7 days, Not Overdue (DueDate >= Now)
            var salesOrders = await _db.SalesOrders
                .AsNoTracking()
                .Where(o => o.Status != SalesOrderStatus.Cancelled &&
                            o.PaymentStatus != PaymentStatus.Paid &&
                            o.DueDate >= now &&
                            o.DueDate <= upcomingWindow)
                .Select(o => new
                {
                    o.Id,
                    o.OrderNumber,
                    o.CustomerId,
                    o.CustomerNameSnapshot,
                    o.DueDate,
                    o.TotalAmount,
                    o.RefundedAmount
                })
                .ToListAsync(ct);

            foreach (var so in salesOrders)
            {
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
                    RemainingAmount = so.TotalAmount - so.RefundedAmount,
                    DaysUntilDue = daysUntil,
                    Message = $"Payment due in {daysUntil} days for Sales Order #{so.OrderNumber} from {so.CustomerNameSnapshot}"
                });
            }

            // 2. Purchase Orders Notifications
            // Criteria: Not Paid, PaymentDeadline exists, within 7 days, Not Overdue
            var purchaseOrders = await _db.PurchaseOrders
                .AsNoTracking()
                .Where(o => o.Status != PurchaseOrderStatus.Cancelled &&
                            o.PaymentStatus != PurchasePaymentStatus.Paid &&
                            o.PaymentDeadline.HasValue &&
                            o.PaymentDeadline.Value >= now &&
                            o.PaymentDeadline.Value <= upcomingWindow)
                .Select(o => new
                {
                    o.Id,
                    o.OrderNumber,
                    o.SupplierId,
                    o.SupplierNameSnapshot,
                    PaymentDeadline = o.PaymentDeadline.Value,
                    o.TotalAmount,
                    o.RefundedAmount
                })
                .ToListAsync(ct);

            foreach (var po in purchaseOrders)
            {
                var daysUntil = (po.PaymentDeadline - now).Days;
                 if (daysUntil < 0) daysUntil = 0;

                notifications.Add(new PaymentNotificationDto
                {
                    NotificationId = $"PO-{po.Id}",
                    OrderId = po.Id,
                    OrderNumber = po.OrderNumber,
                    Type = "Purchase",
                    CounterpartyId = po.SupplierId,
                    CounterpartyName = po.SupplierNameSnapshot,
                    PaymentDeadline = po.PaymentDeadline,
                    RemainingAmount = po.TotalAmount - po.RefundedAmount,
                    DaysUntilDue = daysUntil,
                    Message = $"Payment due in {daysUntil} days for Purchase Order #{po.OrderNumber} to {po.SupplierNameSnapshot}"
                });
            }

            return notifications.OrderBy(n => n.PaymentDeadline);
        }
    }
}
