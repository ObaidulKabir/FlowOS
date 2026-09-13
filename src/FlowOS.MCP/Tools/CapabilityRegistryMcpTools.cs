using System;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Core.Common.Interfaces;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Tools;

public class CapabilityRegistryMcpTools
{
    private readonly ICapabilityRegistryService _capabilityRegistry;

    public CapabilityRegistryMcpTools(ICapabilityRegistryService capabilityRegistry)
    {
        _capabilityRegistry = capabilityRegistry;
    }

    public async Task<CallToolResult> RegisterCapabilityBinding(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);
            var capabilityName = args["capabilityName"]?.ToString()?.Trim();
            var endpointUrl = args["endpointUrl"]?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(capabilityName) || string.IsNullOrWhiteSpace(endpointUrl))
            {
                return McpToolResults.Fail("MCP-ARG-001", "capabilityName and endpointUrl are required.");
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
                capabilityName,
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
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to register capability binding: {ex.Message}");
        }
    }

    public async Task<CallToolResult> ListCapabilityBindings(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);
            var capabilityName = args["capabilityName"]?.ToString()?.Trim();
            bool? enabledOnly = args["enabledOnly"]?.Value<bool>();

            var bindings = await _capabilityRegistry.ListAsync(tenantId, capabilityName, enabledOnly);
            return McpToolResults.Success(new
            {
                totalCount = bindings.Count,
                bindings = bindings.Select(b => new
                {
                    b.Id,
                    b.TenantId,
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
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to list capability bindings: {ex.Message}");
        }
    }

    public async Task<CallToolResult> ValidateCapabilityBinding(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);
            var capabilityName = args["capabilityName"]?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(capabilityName))
            {
                return McpToolResults.Fail("MCP-ARG-001", "capabilityName is required.");
            }

            var (isValid, message, binding) = await _capabilityRegistry.ValidateBindingAsync(tenantId, capabilityName);
            return McpToolResults.Success(new
            {
                capabilityName,
                isValid,
                message,
                binding = binding == null ? null : new
                {
                    binding.Id,
                    binding.TenantId,
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
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to validate capability binding: {ex.Message}");
        }
    }
}
