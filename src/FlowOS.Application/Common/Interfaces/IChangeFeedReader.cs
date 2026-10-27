using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FlowOS.Application.Common.Interfaces;

/// <summary>
/// CR-10c: reads a tenant's events in the order the database stored them, so an integrating
/// system can pull changes with a cursor instead of receiving pushes.
/// </summary>
public interface IChangeFeedReader
{
    /// <param name="afterSequence">Return events strictly after this position (0 = from the start).</param>
    /// <param name="limit">Maximum number of items.</param>
    /// <param name="visibleBeforeUtc">
    /// Events stamped after this time are held back, and so is everything after them, so a
    /// transaction that commits late cannot be skipped by a cursor that has already moved on.
    /// </param>
    Task<ChangeFeedPage> ReadAsync(
        Guid tenantId,
        long afterSequence,
        int limit,
        DateTime visibleBeforeUtc,
        CancellationToken cancellationToken = default);
}

public sealed record ChangeFeedPage(IReadOnlyList<ChangeFeedItem> Items, long LastSequence, bool HasMore);

public sealed record ChangeFeedItem(
    long Sequence,
    Guid EventId,
    string EventType,
    int EventVersion,
    DateTime OccurredAtUtc,
    Guid? CorrelationId,
    Guid? CausationId,
    Guid? WorkflowInstanceId,
    Guid? WorkflowDefinitionId,
    int? WorkflowVersion,
    string? ExternalSourceSystem,
    string? ExternalReference,
    string? PayloadJson,
    IReadOnlyDictionary<string, string> Metadata);
