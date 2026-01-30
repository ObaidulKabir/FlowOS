using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.DTOs;
using FlowOS.Application.Queries;
using FlowOS.Events.Models;
using FlowOS.Infrastructure.Persistence;
using FlowOS.StateMachines.Engine;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Application.Handlers;

public class StateMachineQueryHandlers : IRequestHandler<ValidateStateMachineTransitionQuery, ValidateTransitionResult>
{
    private readonly FlowOSDbContext _context;
    private readonly StateMachineEngine _engine;

    public StateMachineQueryHandlers(FlowOSDbContext context)
    {
        _context = context;
        _engine = new StateMachineEngine();
    }

    public async Task<ValidateTransitionResult> Handle(ValidateStateMachineTransitionQuery request, CancellationToken cancellationToken)
    {
        // 1. Load Definition
        var definition = await _context.StateMachineDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.EntityType == request.EntityType && s.TenantId == request.TenantId, cancellationToken);

        if (definition == null)
        {
            return new ValidateTransitionResult 
            { 
                IsAllowed = false, 
                Reason = $"State Machine for entity '{request.EntityType}' not found.",
                ResultType = "NotFound"
            };
        }

        // 2. Create Dummy Event
        var domainEvent = new StandardEvent(request.TenantId, request.EventType);

        // 3. Validate
        var result = _engine.ValidateTransition(
            definition,
            request.CurrentState,
            domainEvent,
            new FlowOS.StateMachines.Models.ExecutionContext()
        );

        return new ValidateTransitionResult
        {
            IsAllowed = result.IsAllowed,
            Reason = result.Reason,
            NewState = result.MatchedTransition?.ToState,
            ResultType = result.ResultType.ToString()
        };
    }

    // GenericDomainEvent removed
}
