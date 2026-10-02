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
public sealed class FallbackWorkflowAgent : IConversationalAgent
{
    public IWorkflowAgent Primary => _primary;
    private readonly IWorkflowAgent _primary;
    private readonly IReadOnlyList<IWorkflowAgent> _fallbacks;

    public FallbackWorkflowAgent(IWorkflowAgent primary, IReadOnlyList<IWorkflowAgent> fallbacks)
    {
        _primary = primary;
        _fallbacks = fallbacks;
    }

    public int MaxTurns => _primary is IConversationalAgent c ? c.MaxTurns : 1;

    public Task<AgentResult> ExecuteConversationalAsync(DecisionPacket packet, IReadOnlyList<ChatMessage> history, CancellationToken cancellationToken = default)
    {
        return ExecuteConversationalInternalAsync(packet, history, cancellationToken);
    }

    private async Task<AgentResult> ExecuteConversationalInternalAsync(DecisionPacket packet, IReadOnlyList<ChatMessage> history, CancellationToken cancellationToken)
    {
        var result = _primary is IConversationalAgent c 
            ? await c.ExecuteConversationalAsync(packet, history, cancellationToken)
            : await _primary.ExecuteAsync(AgentContext.FromPacket(packet), cancellationToken);

        if (IsRetryableFailure(result))
        {
            foreach (var fallback in _fallbacks)
            {
                var fallbackResult = fallback is IConversationalAgent fc 
                    ? await fc.ExecuteConversationalAsync(packet, history, cancellationToken)
                    : await fallback.ExecuteAsync(AgentContext.FromPacket(packet), cancellationToken);

                if (!IsRetryableFailure(fallbackResult))
                {
                    return fallbackResult;
                }
                
                result = fallbackResult; 
            }
        }

        return result;
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
