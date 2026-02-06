using MediatR;
using FlowOS.Core.Interfaces; // Changed namespace
using FlowOS.Application.Common.Exceptions;
using FlowOS.Application.Common.Interfaces; // Add this back for IPolicySecuredCommand
using FlowOS.Core.Interfaces;
using FlowOS.Security.Interfaces;
using FlowOS.Security.Policies; // Keep this one
using System.Threading;
using System.Threading.Tasks;
using System;
using System.Reflection;
using System.Collections.Generic;
using FlowOS.Application.Common.Attributes;

namespace FlowOS.Application.Behaviors;

public class PolicyEnforcementBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IPolicyProvider _policyProvider;
    private readonly IPolicyEvaluator _policyEvaluator;
    private readonly ICurrentUser _currentUser;
    private readonly ICapabilityService _capabilityService;

    public PolicyEnforcementBehavior(
        IPolicyProvider policyProvider,
        IPolicyEvaluator policyEvaluator,
        ICurrentUser currentUser,
        ICapabilityService capabilityService)
    {
        _policyProvider = policyProvider;
        _policyEvaluator = policyEvaluator;
        _currentUser = currentUser;
        _capabilityService = capabilityService;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is IPolicySecuredCommand securedCommand)
        {
            var tenantId = securedCommand.TenantId;

            // 1. Attribute-Based Capability Check
            var attribute = request.GetType().GetCustomAttribute<RequiresCapabilityAttribute>();
            if (attribute != null)
            {
                var userRoles = _currentUser.Roles ?? new List<string>();
                
                // DEBUG LOGGING
                Console.WriteLine($"[PolicyEnforcement] Checking capability: {attribute.Capability}");
                Console.WriteLine($"[PolicyEnforcement] User: {_currentUser.Id}, Roles: {string.Join(",", userRoles)}");
                Console.WriteLine($"[PolicyEnforcement] Tenant: {tenantId}");

                var capabilities = await _capabilityService.GetCapabilitiesAsync(tenantId, userRoles);
                
                Console.WriteLine($"[PolicyEnforcement] Resolved Capabilities: {string.Join(", ", capabilities)}");

                if (!capabilities.Contains(attribute.Capability))
                {
                    // Fail if user does not have the required capability
                     Console.WriteLine($"[PolicyEnforcement] FAILED: Missing capability {attribute.Capability}");
                     throw new PolicyViolationException("CapabilityCheck", $"Missing required capability: {attribute.Capability}");
                }
                Console.WriteLine($"[PolicyEnforcement] PASSED: Has capability {attribute.Capability}");
            }

            // 2. Dynamic Policy Check
            var context = new PolicyContext
            {
                TenantId = tenantId.ToString(),
                ActorId = _currentUser.Id ?? "anonymous",
                Roles = _currentUser.Roles ?? new(),
                CommandType = request.GetType().Name,
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
