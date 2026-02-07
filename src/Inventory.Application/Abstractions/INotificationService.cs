using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.DTOs;

namespace Inventory.Application.Abstractions
{
    public interface INotificationService
    {
        Task<IEnumerable<PaymentNotificationDto>> GetActiveNotificationsAsync(CancellationToken ct = default);
    }
}
