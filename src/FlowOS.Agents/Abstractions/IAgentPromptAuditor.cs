using FlowOS.Domain.Entities;

namespace FlowOS.Agents.Abstractions;

public interface IAgentPromptAuditor
{
    bool IsEnabled { get; }
    Task TryRecordAsync(AgentPromptAuditRecord record, CancellationToken ct = default);
}
