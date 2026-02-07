namespace Inventory.Web.Models;

public class ErrorViewModel
{
    public string? RequestId { get; set; }
    public string? UserMessage { get; set; }

    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
}
