using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.ReadModels;

namespace FlowOS.Application.Common.Interfaces.Persistence;

public interface IAgentInsightRepository
{
    Task<List<AgentInsightReadModel>> ListByWorkflowInstanceIdsAsync(IEnumerable<Guid> workflowInstanceIds, CancellationToken cancellationToken = default);
    Task<List<AgentInsightReadModel>> ListByWorkflowInstanceIdAsync(Guid workflowInstanceId, CancellationToken cancellationToken = default);
    void Add(AgentInsightReadModel insight);
}
