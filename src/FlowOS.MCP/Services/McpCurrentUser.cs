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
}

public class McpCurrentUser : ICurrentUser
{
    public string? Id => "mcp-agent";

    public Guid TenantId => McpRequestContext.TenantId == Guid.Empty
        ? Guid.Parse("11111111-1111-1111-1111-111111111111")
        : McpRequestContext.TenantId;

    public List<string> Roles
    {
        get
        {
            var role = string.IsNullOrWhiteSpace(McpRequestContext.Role)
                ? "Admin"
                : McpRequestContext.Role!;
            return new List<string> { role };
        }
    }
}
