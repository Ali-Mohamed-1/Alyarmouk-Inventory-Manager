using System;

namespace Inventory.Application.DTOs
{
    public class PaymentNotificationDto
    {
        public string NotificationId { get; set; } = string.Empty;
        public long OrderId { get; set; }
        public string OrderNumber { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // "Sales" or "Purchase"
        public int CounterpartyId { get; set; }
        public string CounterpartyName { get; set; } = string.Empty;
        public DateTimeOffset PaymentDeadline { get; set; }
        public decimal RemainingAmount { get; set; }
        public string Message { get; set; } = string.Empty;
        public int DaysUntilDue { get; set; }
    }
}
