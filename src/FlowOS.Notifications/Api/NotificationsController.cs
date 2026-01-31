using System;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Core.Interfaces;
using FlowOS.Notifications.Application;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Notifications.Api;

// Moved Interface to Application layer (or keep here but public)
// If we want Infrastructure to implement it, it must be visible to Infrastructure.
// Infrastructure DOES NOT reference FlowOS.Notifications.Api (that would be weird).
// Infrastructure references FlowOS.Notifications (which includes Domain, Application, Infrastructure).
// Api is usually the entry point.
// Wait, FlowOS.Notifications.Api IS inside FlowOS.Notifications.csproj?
// Yes.
// And FlowOS.Infrastructure depends on FlowOS.Notifications.
// So FlowOS.Infrastructure CAN see types in FlowOS.Notifications.
// But NotificationsController is in namespace FlowOS.Notifications.Api.
// Is FlowOS.Infrastructure referencing FlowOS.Notifications? Yes.
// So why can't it find INotificationQueryService?
// Because INotificationQueryService is defined in NotificationsController.cs?
// If it's in the same file, it might be fine if namespace matches.
// But FlowOS.Infrastructure has `using FlowOS.Notifications.Api;`? Probably not.
// We should move INotificationQueryService to FlowOS.Notifications.Application.

[ApiController]
[Route("api/notifications")]
public class NotificationsController : ControllerBase
{
    private readonly INotificationQueryService _queryService; // Use abstraction
    private readonly ICurrentUser _currentUser;
    private readonly NotificationStreamService _streamService;

    public NotificationsController(INotificationQueryService queryService, ICurrentUser currentUser, NotificationStreamService streamService)
    {
        _queryService = queryService;
        _currentUser = currentUser;
        _streamService = streamService;
    }

    [HttpGet]
    public async Task<IActionResult> GetNotifications()
    {
        var tenantId = _currentUser.TenantId;
        var notifications = await _queryService.GetNotificationsAsync(tenantId);
        return Ok(notifications);
    }
    
    // ... rest of stream logic ...
    [HttpGet("stream")]
    public async Task GetNotificationStream()
    {
        var tenantId = _currentUser.TenantId;
        
        Response.Headers.Add("Content-Type", "text/event-stream");
        Response.Headers.Add("Cache-Control", "no-cache");
        Response.Headers.Add("Connection", "keep-alive");

        var client = new StreamClient(new System.IO.StreamWriter(Response.Body) { AutoFlush = true });
        _streamService.AddClient(tenantId, client);

        try
        {
            // Keep connection open
            while (!HttpContext.RequestAborted.IsCancellationRequested)
            {
                await Task.Delay(1000); // Heartbeat / Keep-alive check
            }
        }
        finally
        {
            _streamService.RemoveClient(tenantId, client);
        }
    }
}

