using FlowOS.Agents.Abstractions;
using FlowOS.Application.Common.Interfaces;

namespace FlowOS.Application.Services;

public sealed class AgentToolHost : IAgentToolHost
{
    private readonly IReadOnlyDictionary<string, IAgentResourcePlugin> _plugins;
    private readonly ICapabilityInvoker? _invoker;

    public AgentToolHost(
        IEnumerable<IAgentResourcePlugin> plugins,
        ICapabilityInvoker? invoker = null)
    {
        _plugins = plugins.ToDictionary(plugin => plugin.Name, plugin => plugin, StringComparer.OrdinalIgnoreCase);
        _invoker = invoker;
    }

    public async Task<DecisionPacket> PrefetchAsync(
        DecisionPacket packet,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var tool in packet.DeclaredTools)
        {
            if (!tool.Prefetch) continue;

            // Security gate: Mutating tools (write, notify) cannot be silently prefetched
            if (string.Equals(tool.SideEffect, "write", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tool.SideEffect, "notify", StringComparison.OrdinalIgnoreCase))
            {
                results[tool.Name] = new { ok = false, error = $"Tool '{tool.Name}' has side-effect '{tool.SideEffect}' and cannot be prefetched." };
                continue;
            }

            if (string.IsNullOrWhiteSpace(tool.Capability))
            {
                results[tool.Name] = new { ok = false, error = "No capability bound for this tool." };
                continue;
            }

            AgentResourceResult executed;
            if (_plugins.TryGetValue(tool.Provider ?? tool.Kind, out var plugin) ||
                _plugins.TryGetValue(AgentToolCatalog.ResourcePluginName(tool.Name), out plugin))
            {
                executed = await plugin.ExecuteAsync(
                    new AgentResourceRequest(
                        packet.TenantId,
                        packet.WorkflowInstanceId,
                        packet.CurrentStepId,
                        tool.Name,
                        tool.Capability,
                        packet.CanonicalContext,
                        packet.EventPayloads),
                    cancellationToken);
            }
            else if (_invoker != null)
            {
                var invoked = await _invoker.InvokeAsync(
                    packet.TenantId,
                    tool.Capability,
                    "lookup",
                    packet.CanonicalContext,
                    packet.WorkflowInstanceId,
                    packet.CurrentStepId,
                    cancellationToken);
                executed = new AgentResourceResult(
                    invoked.Ok, tool.Name, tool.Capability, invoked.Parsed ?? invoked.Body, invoked.Error);
            }
            else
            {
                results[tool.Name] = new { ok = false, error = $"No resource plugin registered for '{tool.Name}'." };
                continue;
            }

            results[tool.Name] = new
            {
                ok = executed.Ok,
                capability = executed.CapabilityName,
                data = executed.Data,
                error = executed.Error
            };
        }

        return packet with { ToolResults = results };
    }

    public async Task<string> InvokeToolAsync(
        DecisionPacket packet,
        string toolName,
        string? argumentsJson,
        CancellationToken cancellationToken = default)
    {
        var tool = packet.DeclaredTools?.FirstOrDefault(t => t.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase));
        if (tool == null)
        {
            return $"{{\"ok\":false,\"error\":\"Tool '{toolName}' is not declared for this packet.\"}}";
        }

        if (string.IsNullOrWhiteSpace(tool.Capability))
        {
            return $"{{\"ok\":false,\"error\":\"No capability bound for this tool.\"}}";
        }

        object? payload = null;
        if (!string.IsNullOrWhiteSpace(argumentsJson))
        {
            try
            {
                payload = System.Text.Json.JsonSerializer.Deserialize<object>(argumentsJson);
            }
            catch
            {
                // Ignore parse errors, capability might expect raw string
                payload = argumentsJson;
            }
        }
        else
        {
            payload = packet.CanonicalContext;
        }

        AgentResourceResult executed;
        if (_plugins.TryGetValue(tool.Provider ?? tool.Kind, out var plugin) ||
            _plugins.TryGetValue(AgentToolCatalog.ResourcePluginName(tool.Name), out plugin))
        {
            executed = await plugin.ExecuteAsync(
                new AgentResourceRequest(
                    packet.TenantId,
                    packet.WorkflowInstanceId,
                    packet.CurrentStepId,
                    tool.Name,
                    tool.Capability,
                    packet.CanonicalContext,
                    packet.EventPayloads),
                cancellationToken);
        }
        else if (_invoker != null)
        {
            var invoked = await _invoker.InvokeAsync(
                packet.TenantId,
                tool.Capability,
                tool.SideEffect,
                payload,
                packet.WorkflowInstanceId,
                packet.CurrentStepId,
                cancellationToken);
                
            executed = new AgentResourceResult(
                invoked.Ok, tool.Name, tool.Capability, invoked.Parsed ?? invoked.Body, invoked.Error);
        }
        else
        {
            return $"{{\"ok\":false,\"error\":\"No resource plugin registered for '{tool.Name}'.\"}}";
        }

        var resultObj = new
        {
            ok = executed.Ok,
            capability = executed.CapabilityName,
            data = executed.Data,
            error = executed.Error
        };

        return System.Text.Json.JsonSerializer.Serialize(resultObj);
    }
}
