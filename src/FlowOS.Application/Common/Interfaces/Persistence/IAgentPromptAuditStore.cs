using FlowOS.Domain.Entities;

namespace FlowOS.Application.Common.Interfaces.Persistence;

public interface IAgentPromptAuditStore
{
    Task RecordAsync(AgentPromptAuditRecord record, CancellationToken ct = default);
}
