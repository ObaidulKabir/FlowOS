using MediatR;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Common.Exceptions;
using FlowOS.Security.Policies;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace FlowOS.Application.Behaviors;

public class PolicyEnforcementBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IPolicyProvider _policyProvider;
    private readonly IPolicyEvaluator _policyEvaluator;
    private readonly ICurrentUser _currentUser;

    public PolicyEnforcementBehavior(
        IPolicyProvider policyProvider,
        IPolicyEvaluator policyEvaluator,
        ICurrentUser currentUser)
    {
        _policyProvider = policyProvider;
        _policyEvaluator = policyEvaluator;
        _currentUser = currentUser;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is IPolicySecuredCommand securedCommand)
        {
            var context = new PolicyContext
            {
                TenantId = securedCommand.TenantId.ToString(),
                ActorId = _currentUser.Id ?? "anonymous",
                Roles = _currentUser.Roles ?? new(),
                CommandType = request.GetType().Name,
                // Metadata could be populated via reflection or extra interface if needed
            };

            var policies = await _policyProvider.GetApplicablePoliciesAsync(context);

            foreach (var policy in policies)
            {
                var result = _policyEvaluator.Evaluate(policy, context);
                if (!result.IsAllowed)
                {
                    throw new PolicyViolationException(policy.Name, result.Reason);
                }
            }
        }

        return await next();
    }
}
