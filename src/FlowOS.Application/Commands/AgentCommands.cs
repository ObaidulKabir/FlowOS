using System;
using MediatR;
using FlowOS.Application.Common.Interfaces;

namespace FlowOS.Application.Commands;

public record PublishAgentInsightCommand(
    Guid TenantId,
    Guid WorkflowInstanceId,
    string AgentId,
    string Insight,
    string ContextObjective,
    Guid? CorrelationId = null
) : IRequest<bool>, IPolicySecuredCommand;
