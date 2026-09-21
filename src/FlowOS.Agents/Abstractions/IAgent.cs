using System.Threading;
using System.Threading.Tasks;

namespace FlowOS.Agents.Abstractions;

public interface IAgent
{
    Task<AgentResult> ExecuteAsync(AgentContext context);

    Task<AgentResult> ExecuteAsync(
        AgentContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ExecuteAsync(context);
    }
}
