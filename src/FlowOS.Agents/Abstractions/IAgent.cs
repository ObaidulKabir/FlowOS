using System.Threading.Tasks;

namespace FlowOS.Agents.Abstractions;

public interface IAgent
{
    Task<AgentResult> ExecuteAsync(AgentContext context);
}
