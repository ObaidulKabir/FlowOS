using System;
using System.Collections.Generic;
using System.Threading;
using FlowOS.Core.Interfaces;

namespace FlowOS.MCP.Services;

/// <summary>
/// Ambient tenant/role for the duration of a single MCP tool call or HTTP request.
/// </summary>
public static class McpRequestContext
{
    private static readonly AsyncLocal<Guid> TenantIdCurrent = new();
    private static readonly AsyncLocal<string?> RoleCurrent = new();
    private static readonly AsyncLocal<bool> AuthenticatedTransportCurrent = new();

    public static Guid TenantId
    {
        get => TenantIdCurrent.Value;
        set => TenantIdCurrent.Value = value;
    }

    public static string? Role
    {
        get => RoleCurrent.Value;
        set => RoleCurrent.Value = value;
    }

    public static bool IsAuthenticatedTransport
    {
        get => AuthenticatedTransportCurrent.Value;
        set => AuthenticatedTransportCurrent.Value = value;
    }

    public static void Clear()
    {
        TenantIdCurrent.Value = Guid.Empty;
        RoleCurrent.Value = null;
        AuthenticatedTransportCurrent.Value = false;
    }
}

public class McpCurrentUser : ICurrentUser
{
    public string? Id => "mcp-agent";

    public Guid TenantId => McpRequestContext.TenantId;

    public List<string> Roles
    {
        get
        {
            return string.IsNullOrWhiteSpace(McpRequestContext.Role)
                ? new List<string>()
                : new List<string> { McpRequestContext.Role! };
        }
    }
}
