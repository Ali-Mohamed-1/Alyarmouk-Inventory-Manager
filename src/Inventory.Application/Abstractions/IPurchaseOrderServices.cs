using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.PurchaseOrder;

namespace Inventory.Application.Abstractions
{
    public interface IPurchaseOrderServices
    {
        Task<long> CreateAsync(CreatePurchaseOrderRequest req, UserContext user, CancellationToken ct = default);
        // Add Get/List methods as needed later, focusing on Create for now as per plan
    }
}
