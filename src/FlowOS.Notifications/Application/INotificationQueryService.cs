using System;
using System.Threading.Tasks;

namespace FlowOS.Notifications.Application;

public interface INotificationQueryService
{
    Task<object> GetNotificationsAsync(Guid tenantId);
}
