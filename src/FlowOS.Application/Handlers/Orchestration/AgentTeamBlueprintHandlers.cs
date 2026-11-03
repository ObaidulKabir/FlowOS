using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Commands.Orchestration;
using FlowOS.Domain.Services;
using FlowOS.Domain.Validation;
using MediatR;

namespace FlowOS.Application.Handlers.Orchestration;

/// <summary>ADR-007 §8: validation-only handler, ahead of any AgentTeamClass persistence.</summary>
public sealed class AgentTeamBlueprintHandlers :
    IRequestHandler<ValidateAgentTeamBlueprintCommand, ValidationResult>
{
    private readonly IAgentTeamBlueprintValidator _validator;

    public AgentTeamBlueprintHandlers(IAgentTeamBlueprintValidator validator)
    {
        _validator = validator;
    }

    public Task<ValidationResult> Handle(ValidateAgentTeamBlueprintCommand request, CancellationToken cancellationToken) =>
        Task.FromResult(_validator.Validate(request.Definition));
}
