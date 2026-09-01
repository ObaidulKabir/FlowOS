using System;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Core.Interfaces;
using FlowOS.Notifications.Application;
using Microsoft.AspNetCore.Http;
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
[Microsoft.AspNetCore.Authorization.Authorize]
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
        var userId = Guid.TryParse(_currentUser.Id, out var uid) ? uid : Guid.Empty;
        var notifications = await _queryService.GetNotificationsAsync(tenantId, userId);
        return Ok(notifications);
    }
    
    [HttpPut("{id}/read")]
    public async Task<IActionResult> MarkAsRead(Guid id)
    {
        var tenantId = _currentUser.TenantId;
        var userId = Guid.TryParse(_currentUser.Id, out var uid) ? uid : Guid.Empty;
        await _queryService.MarkAsReadAsync(tenantId, userId, id);
        return NoContent();
    }
    
    // ... rest of stream logic ...
    [HttpGet("stream")]
    public async Task GetNotificationStream()
    {
        var tenantId = _currentUser.TenantId;
        var userId = Guid.TryParse(_currentUser.Id, out var uid) ? uid : Guid.Empty;
        
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        // Use StreamClient but associate it with userId?
        // Right now AddClient just takes tenantId. We'll leave the stream logic as tenant-broadcast for now, 
        // or update stream service to filter by userId.
        var client = new StreamClient(new System.IO.StreamWriter(Response.Body) { AutoFlush = true }, userId);
        _streamService.AddClient(tenantId, client);

        try
        {
            // Keep connection open
            while (!HttpContext.RequestAborted.IsCancellationRequested)
            {
                await Task.Delay(15000, HttpContext.RequestAborted); // 15s heartbeat
                await client.WriteMessageAsync(":\n\n"); // SSE comment as ping
            }
        }
        catch (TaskCanceledException)
        {
            // Client disconnected normally
        }
        finally
        {
            _streamService.RemoveClient(tenantId, client);
            client.Dispose();
        }
    }
}

