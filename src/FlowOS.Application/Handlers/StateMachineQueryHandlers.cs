using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Application.DTOs;
using FlowOS.Application.Queries;
using FlowOS.Events.Models;
using FlowOS.StateMachines.Engine;
using MediatR;

namespace FlowOS.Application.Handlers;

public class StateMachineQueryHandlers : IRequestHandler<ValidateStateMachineTransitionQuery, ValidateTransitionResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly StateMachineEngine _engine;

    public StateMachineQueryHandlers(IUnitOfWork unitOfWork, StateMachineEngine engine)
    {
        _unitOfWork = unitOfWork;
        _engine = engine;
    }

    public async Task<ValidateTransitionResult> Handle(ValidateStateMachineTransitionQuery request, CancellationToken cancellationToken)
    {
        var definition = await _unitOfWork.StateMachines
            .GetByEntityTypeAndTenantAsync(request.EntityType, request.TenantId, cancellationToken);

        if (definition == null)
        {
            return new ValidateTransitionResult 
            { 
                IsAllowed = false, 
                Reason = $"State Machine for entity '{request.EntityType}' not found.",
                ResultType = "NotFound"
            };
        }

        var domainEvent = new StandardEvent(request.TenantId, request.EventType);

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
}
