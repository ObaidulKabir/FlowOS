using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FlowOS.Application.Common.Interfaces;

/// <summary>
/// Publishes filesystem-based FlowOS configuration (events, state machines, workflows) into persistence.
/// </summary>
public interface IConfigurationPublisher
{
    /// <summary>
    /// Resolves the first existing config root among known candidates (and optional extras).
    /// </summary>
    string? ResolveConfigRoot(IEnumerable<string>? additionalCandidates = null);

    Task PublishAsync(Guid tenantId, string configRoot, CancellationToken cancellationToken = default);
}
