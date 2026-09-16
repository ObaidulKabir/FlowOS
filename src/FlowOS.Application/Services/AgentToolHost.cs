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
}
