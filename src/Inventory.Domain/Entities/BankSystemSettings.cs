namespace Inventory.Domain.Entities;

public class BankSystemSettings
{
    public int Id { get; set; }
    
    /// <summary>
    /// The starting balance of the bank account before system recording began.
    /// This is added to the calculated cash flow to determine the final Bank Balance.
    /// </summary>
    public decimal BankBaseBalance { get; set; }

    public DateTimeOffset LastUpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
