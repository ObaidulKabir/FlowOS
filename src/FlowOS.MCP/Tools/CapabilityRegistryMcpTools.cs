using System;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Core.Common.Interfaces;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Tools;

/// <summary>
/// Connector registry tools. "Connector" is the current name for an outbound tenant worker;
/// the older "capability binding" wording stays accepted on the wire.
/// </summary>
public class CapabilityRegistryMcpTools
{
    private readonly ICapabilityRegistryService _capabilityRegistry;

    public CapabilityRegistryMcpTools(ICapabilityRegistryService capabilityRegistry)
    {
        _capabilityRegistry = capabilityRegistry;
    }

    private static string? ReadConnectorName(JObject args)
    {
        var name = args["connectorName"]?.ToString()?.Trim();
        return string.IsNullOrWhiteSpace(name)
            ? args["capabilityName"]?.ToString()?.Trim()
            : name;
    }

    public async Task<CallToolResult> RegisterCapabilityBinding(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);
            var connectorName = ReadConnectorName(args);
            var endpointUrl = args["endpointUrl"]?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(connectorName) || string.IsNullOrWhiteSpace(endpointUrl))
            {
                return McpToolResults.Fail("MCP-ARG-001", "connectorName and endpointUrl are required.");
            }

            var transport = args["transport"]?.ToString()?.Trim() ?? "http";
            var timeoutMs = args["timeoutMs"]?.Value<int>() ?? 10000;
            var authRef = args["authRef"]?.ToString()?.Trim();
            var requestSchemaVersion = args["requestSchemaVersion"]?.ToString()?.Trim();
            var responseSchemaVersion = args["responseSchemaVersion"]?.ToString()?.Trim();
            var retryPolicy = args["retryPolicy"]?.ToString()?.Trim() ?? "default";
            var isEnabled = args["isEnabled"]?.Value<bool>() ?? true;

            var binding = await _capabilityRegistry.UpsertAsync(
                tenantId,
                connectorName,
                endpointUrl,
                transport,
                timeoutMs,
                authRef,
                requestSchemaVersion,
                responseSchemaVersion,
                retryPolicy,
                isEnabled);

            return McpToolResults.Success(new
            {
                binding.Id,
                binding.TenantId,
                ConnectorName = binding.CapabilityName,
                binding.CapabilityName,
                binding.Transport,
                binding.EndpointUrl,
                binding.AuthRef,
                binding.RequestSchemaVersion,
                binding.ResponseSchemaVersion,
                binding.RetryPolicy,
                binding.TimeoutMs,
                binding.IsEnabled,
                binding.CreatedAtUtc,
                binding.UpdatedAtUtc
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return McpToolResults.Fail("MCP-ARG-001", ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to register connector: {ex.Message}");
        }
    }

    public async Task<CallToolResult> ListCapabilityBindings(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);
            var connectorName = ReadConnectorName(args);
            bool? enabledOnly = args["enabledOnly"]?.Value<bool>();

            var bindings = await _capabilityRegistry.ListAsync(tenantId, connectorName, enabledOnly);
            return McpToolResults.Success(new
            {
                totalCount = bindings.Count,
                connectors = bindings.Select(b => new
                {
                    b.Id,
                    b.TenantId,
                    ConnectorName = b.CapabilityName,
                    b.CapabilityName,
                    b.Transport,
                    b.EndpointUrl,
                    b.AuthRef,
                    b.RequestSchemaVersion,
                    b.ResponseSchemaVersion,
                    b.RetryPolicy,
                    b.TimeoutMs,
                    b.IsEnabled,
                    b.CreatedAtUtc,
                    b.UpdatedAtUtc
                }).ToList(),
                bindings = bindings.Select(b => new
                {
                    b.Id,
                    b.TenantId,
                    ConnectorName = b.CapabilityName,
                    b.CapabilityName,
                    b.Transport,
                    b.EndpointUrl,
                    b.AuthRef,
                    b.RequestSchemaVersion,
                    b.ResponseSchemaVersion,
                    b.RetryPolicy,
                    b.TimeoutMs,
                    b.IsEnabled,
                    b.CreatedAtUtc,
                    b.UpdatedAtUtc
                }).ToList()
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to list connectors: {ex.Message}");
        }
    }

    public async Task<CallToolResult> ValidateCapabilityBinding(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);
            var connectorName = ReadConnectorName(args);
            if (string.IsNullOrWhiteSpace(connectorName))
            {
                return McpToolResults.Fail("MCP-ARG-001", "connectorName is required.");
            }

            var (isValid, message, binding) = await _capabilityRegistry.ValidateBindingAsync(tenantId, connectorName);
            return McpToolResults.Success(new
            {
                connectorName,
                capabilityName = connectorName,
                isValid,
                message,
                binding = binding == null ? null : new
                {
                    binding.Id,
                    binding.TenantId,
                    ConnectorName = binding.CapabilityName,
                    binding.CapabilityName,
                    binding.Transport,
                    binding.EndpointUrl,
                    binding.AuthRef,
                    binding.RequestSchemaVersion,
                    binding.ResponseSchemaVersion,
                    binding.RetryPolicy,
                    binding.TimeoutMs,
                    binding.IsEnabled
                }
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to validate connector: {ex.Message}");
        }
    }
}
