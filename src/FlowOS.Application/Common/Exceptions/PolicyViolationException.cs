using System;

namespace FlowOS.Application.Common.Exceptions;

public class PolicyViolationException : Exception
{
    public string? PolicyName { get; }
    public string Reason { get; }

    public PolicyViolationException(string message) : base(message)
    {
        Reason = message;
    }

    public PolicyViolationException(string policyName, string reason) 
        : base($"Policy '{policyName}' denied execution: {reason}")
    {
        PolicyName = policyName;
        Reason = reason;
    }
}
