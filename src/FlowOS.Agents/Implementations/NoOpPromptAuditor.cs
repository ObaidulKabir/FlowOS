using FlowOS.Agents.Abstractions;
using FlowOS.Domain.Entities;

namespace FlowOS.Agents.Implementations;

internal sealed class NoOpPromptAuditor : IAgentPromptAuditor
{
    public bool IsEnabled => false;

    public Task TryRecordAsync(AgentPromptAuditRecord record, CancellationToken ct = default) =>
        Task.CompletedTask;
}
