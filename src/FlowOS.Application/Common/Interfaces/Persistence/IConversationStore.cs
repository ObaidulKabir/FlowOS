using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Agents.Abstractions;

namespace FlowOS.Application.Common.Interfaces.Persistence;

public interface IConversationStore
{
    Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string stepId,
        CancellationToken cancellationToken = default);

    Task AppendMessageAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string stepId,
        ChatMessage message,
        CancellationToken cancellationToken = default);
        
    Task AppendMessagesAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string stepId,
        IEnumerable<ChatMessage> messages,
        CancellationToken cancellationToken = default);
}
