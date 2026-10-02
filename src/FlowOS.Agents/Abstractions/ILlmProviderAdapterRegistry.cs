using FlowOS.Agents.Implementations.Adapters;
using FlowOS.Core.Common.Models;

namespace FlowOS.Agents.Abstractions;

public interface ILlmProviderAdapterRegistry
{
    ILlmProviderAdapter GetAdapter(string? providerName, AgentProviderConfiguration? config = null);
}
