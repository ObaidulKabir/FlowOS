using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Agents.Abstractions;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Infrastructure.Persistence.Repositories;

internal sealed class ConversationStore : IConversationStore
{
    private readonly FlowOSDbContext _dbContext;

    public ConversationStore(FlowOSDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string stepId,
        CancellationToken cancellationToken = default)
    {
        var records = await _dbContext.ConversationMessages
            .Where(m => m.TenantId == tenantId && m.WorkflowInstanceId == workflowInstanceId && m.StepId == stepId)
            .OrderBy(m => m.Timestamp)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return records.Select(m => new ChatMessage(
            Enum.Parse<ChatMessageRole>(m.Role, true),
            m.Content,
            m.Name,
            m.Timestamp
        )).ToList();
    }

    public async Task AppendMessageAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string stepId,
        ChatMessage message,
        CancellationToken cancellationToken = default)
    {
        await AppendMessagesAsync(tenantId, workflowInstanceId, stepId, new[] { message }, cancellationToken);
    }

    public async Task AppendMessagesAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string stepId,
        IEnumerable<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var records = messages.Select(m => new ConversationMessageRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WorkflowInstanceId = workflowInstanceId,
            StepId = stepId,
            Role = m.Role.ToString(),
            Content = m.Content,
            Name = m.Name,
            Timestamp = m.Timestamp ?? DateTimeOffset.UtcNow
        });

        _dbContext.ConversationMessages.AddRange(records);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
