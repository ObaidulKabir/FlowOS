using System.Threading;
using System.Threading.Tasks;

namespace FlowOS.Agents.Abstractions;

/// <summary>
/// Provides a mechanism for an autonomous agent to dynamically invoke a registered tool (capability).
/// </summary>
public interface IAgentToolInvoker
{
    /// <summary>
    /// Invokes a tool by name with the given JSON arguments.
    /// </summary>
    /// <param name="toolName">The name of the tool to invoke.</param>
    /// <param name="argumentsJson">The arguments for the tool, serialized as a JSON object.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A JSON string representing the result of the tool invocation, or an error message.</returns>
    Task<string> InvokeToolAsync(
        string toolName,
        string? argumentsJson,
        CancellationToken cancellationToken = default);
}
