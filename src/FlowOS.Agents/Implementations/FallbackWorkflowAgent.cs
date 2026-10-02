using FlowOS.Agents.Abstractions;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FlowOS.Agents.Implementations;

/// <summary>
/// Wraps a primary workflow agent and a chain of fallback agents.
/// If the primary agent fails with a rate-limit or unavailability error, 
/// the next fallback agent in the chain is attempted.
/// </summary>
public sealed class FallbackWorkflowAgent : IWorkflowAgent
{
    private readonly IWorkflowAgent _primary;
    private readonly IReadOnlyList<IWorkflowAgent> _fallbacks;

    public FallbackWorkflowAgent(IWorkflowAgent primary, IReadOnlyList<IWorkflowAgent> fallbacks)
    {
        _primary = primary;
        _fallbacks = fallbacks;
    }

    public Task<AgentResult> ExecuteAsync(AgentContext context) =>
        ExecuteAsync(context, CancellationToken.None);

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken cancellationToken)
    {
        var result = await _primary.ExecuteAsync(context, cancellationToken);

        if (IsRetryableFailure(result))
        {
            foreach (var fallback in _fallbacks)
            {
                var fallbackResult = await fallback.ExecuteAsync(context, cancellationToken);
                if (!IsRetryableFailure(fallbackResult))
                {
                    return fallbackResult;
                }
                
                // If this fallback also failed with a retryable error, continue to the next one
                result = fallbackResult; 
            }
        }

        return result;
    }

    private static bool IsRetryableFailure(AgentResult result)
    {
        if (result.Success) return false;

        return result.FailureCode == AgentFailureCodes.ProviderRateLimit ||
               result.FailureCode == AgentFailureCodes.ProviderUnavailable ||
               result.FailureCode == AgentFailureCodes.ProviderTimeout;
    }
}
