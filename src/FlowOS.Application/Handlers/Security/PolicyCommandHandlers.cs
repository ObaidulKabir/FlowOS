using System;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Commands.Security;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Security.Models;
using MediatR;

namespace FlowOS.Application.Handlers.Security;

public class PolicyCommandHandlers :
    IRequestHandler<CreatePolicyCommand, Guid>,
    IRequestHandler<GetPolicyByIdQuery, Policy?>
{
    private readonly IUnitOfWork _unitOfWork;

    public PolicyCommandHandlers(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Guid> Handle(CreatePolicyCommand request, CancellationToken cancellationToken)
    {
        if (await _unitOfWork.Policies.ExistsByNameAsync(request.TenantId, request.Name, cancellationToken))
            throw new InvalidOperationException($"Policy '{request.Name}' already exists.");

        var policy = new Policy(request.TenantId, request.Name, request.ConditionJson);
        _unitOfWork.Policies.Add(policy);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return policy.Id;
    }

    public Task<Policy?> Handle(GetPolicyByIdQuery request, CancellationToken cancellationToken)
        => _unitOfWork.Policies.GetByIdAsync(request.Id, request.TenantId, cancellationToken);
}
