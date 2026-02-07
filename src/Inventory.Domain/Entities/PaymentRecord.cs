using System;

namespace Inventory.Domain.Entities;

public enum OrderType
{
    SalesQuery = 1, // "Sales" might be reserved or generic, using SalesOrder/PurchaseOrder is clearer
    SalesOrder = 1,
    PurchaseOrder = 2
}

public enum PaymentRecordType
{
    Payment = 1,
    Refund = 2
}

public class PaymentRecord
{
    public long Id { get; set; }
    
    public OrderType OrderType { get; set; }
    
    public long? SalesOrderId { get; set; }
    public SalesOrder? SalesOrder { get; set; }
    
    public long? PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }
    
    public decimal Amount { get; set; }
    
    public DateTimeOffset PaymentDate { get; set; }
    
    public PaymentMethod PaymentMethod { get; set; }
    
    public PaymentRecordType PaymentType { get; set; } = PaymentRecordType.Payment;
    
    /// <summary>
    /// Reference for the payment (e.g., Bank Transfer ID, Check Number).
    /// </summary>
    public string? Reference { get; set; }
    
    public string? Note { get; set; }

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedByUserId { get; set; } = "";
}
