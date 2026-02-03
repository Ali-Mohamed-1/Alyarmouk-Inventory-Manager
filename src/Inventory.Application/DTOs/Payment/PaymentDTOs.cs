using System;
using Inventory.Domain.Entities;

namespace Inventory.Application.DTOs.Payment
{
    public class PaymentRecordDto
    {
        public long Id { get; set; }
        public decimal Amount { get; set; }
        public DateTimeOffset PaymentDate { get; set; }
        public PaymentMethod PaymentMethod { get; set; }
        public PaymentRecordType PaymentType { get; set; }
        public string? Reference { get; set; }
        public string? Note { get; set; }
        public string CreatedByUserId { get; set; } = "";
    }

    public class CreatePaymentRequest
    {
        public decimal Amount { get; set; }
        public DateTimeOffset PaymentDate { get; set; } = DateTimeOffset.UtcNow;
        public PaymentMethod PaymentMethod { get; set; }
        public string? Reference { get; set; }
        public string? Note { get; set; }
    }
}
