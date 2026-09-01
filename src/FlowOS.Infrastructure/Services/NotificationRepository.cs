using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Notifications.Application;
using FlowOS.Notifications.Domain;
using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Infrastructure.Services;

public class NotificationRepository : INotificationRepository, INotificationQueryService
{
    private readonly FlowOSDbContext _context;

    public NotificationRepository(FlowOSDbContext context)
    {
        _context = context;
    }

    // Command Side (For Projector)
    public void Add(Notification notification)
    {
        _context.Notifications.Add(notification);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }

    // Query Side (For Controller)
    public async Task<object> GetNotificationsAsync(Guid tenantId, Guid userId)
    {
        return await _context.Notifications
            .AsNoTracking()
            .Where(n => n.TenantId == tenantId && (n.TargetUserId == null || n.TargetUserId == userId))
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .Select(n => new 
            {
                n.Id,
                n.Message,
                n.Severity,
                n.CreatedAt,
                n.EventType,
                n.IsRead
            })
            .ToListAsync();
    }

    public async Task MarkAsReadAsync(Guid tenantId, Guid userId, Guid notificationId)
    {
        var notification = await _context.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.TenantId == tenantId && (n.TargetUserId == null || n.TargetUserId == userId));
            
        if (notification != null && !notification.IsRead)
        {
            notification.MarkAsRead();
            await _context.SaveChangesAsync();
        }
    }
}
