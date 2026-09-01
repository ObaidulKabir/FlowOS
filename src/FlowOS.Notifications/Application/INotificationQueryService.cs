using System;
using System.Threading.Tasks;

namespace FlowOS.Notifications.Application;

public interface INotificationQueryService
{
    Task<object> GetNotificationsAsync(Guid tenantId, Guid userId);
    Task MarkAsReadAsync(Guid tenantId, Guid userId, Guid notificationId);
}
