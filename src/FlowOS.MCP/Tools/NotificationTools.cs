using System;
using System.Threading.Tasks;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using FlowOS.Notifications.Application;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Tools;

public class NotificationTools
{
    private readonly INotificationQueryService _notificationQueryService;

    public NotificationTools(INotificationQueryService notificationQueryService)
    {
        _notificationQueryService = notificationQueryService;
    }

    public async Task<CallToolResult> ListNotifications(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);
            var userIdStr = args["userId"]?.ToString();
            var userId = Guid.TryParse(userIdStr, out var uid) ? uid : Guid.Empty;

            var notifications = await _notificationQueryService.GetNotificationsAsync(tenantId, userId);
            return McpToolResults.Success(new { notifications });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception)
        {
            return McpToolResults.Fail("MCP-INTERNAL", "Failed to retrieve notifications.");
        }
    }

    public async Task<CallToolResult> MarkNotificationAsRead(JObject args)
    {
        try
        {
            var idStr = args["id"]?.ToString();
            if (string.IsNullOrWhiteSpace(idStr) || !Guid.TryParse(idStr, out var id))
                return McpToolResults.Fail("MCP-ARG-002", "id must be a valid UUID.");

            var tenantId = McpTenantResolver.ResolveRequired(args);
            var userIdStr = args["userId"]?.ToString();
            var userId = Guid.TryParse(userIdStr, out var uid) ? uid : Guid.Empty;

            await _notificationQueryService.MarkAsReadAsync(tenantId, userId, id);
            return McpToolResults.Success(new { success = true, message = "Notification marked as read." });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception)
        {
            return McpToolResults.Fail("MCP-INTERNAL", "Failed to mark notification as read.");
        }
    }
}
