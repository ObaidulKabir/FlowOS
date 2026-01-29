using System;

namespace FlowOS.Application.Common.Exceptions;

public class PolicyViolationException : Exception
{
    public PolicyViolationException(string message) : base(message)
    {
    }

    public PolicyViolationException(string policyName, string reason) 
        : base($"Policy '{policyName}' denied execution: {reason}")
    {
    }
}
