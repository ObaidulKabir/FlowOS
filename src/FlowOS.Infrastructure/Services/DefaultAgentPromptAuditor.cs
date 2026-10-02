using FlowOS.Agents.Abstractions;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Domain.Entities;

namespace FlowOS.Infrastructure.Services;

internal sealed class DefaultAgentPromptAuditor : IAgentPromptAuditor
{
    private readonly IAgentPromptAuditStore _store;

    public DefaultAgentPromptAuditor(IAgentPromptAuditStore store) => _store = store;

    public bool IsEnabled => true;

    public async Task TryRecordAsync(AgentPromptAuditRecord record, CancellationToken ct = default)
    {
        try
        {
            await _store.RecordAsync(record, ct);
        }
        catch
        {
        }
    }
}
