using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Infrastructure.Services;

public sealed class ChangeFeedReader : IChangeFeedReader
{
    private const string PayloadKey = "Payload";
    private readonly FlowOSDbContext _db;

    public ChangeFeedReader(FlowOSDbContext db)
    {
        _db = db;
    }

    public async Task<ChangeFeedPage> ReadAsync(
        Guid tenantId,
        long afterSequence,
        int limit,
        DateTime visibleBeforeUtc,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit));

        var rows = await _db.Events
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && EF.Property<long>(e, EventConfiguration.SequenceProperty) > afterSequence)
            .OrderBy(e => EF.Property<long>(e, EventConfiguration.SequenceProperty))
            .Select(e => new { Event = e, Sequence = EF.Property<long>(e, EventConfiguration.SequenceProperty) })
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        // Stop at the first event that is too recent: everything after it waits for the next poll.
        var visible = rows.TakeWhile(r => r.Event.Timestamp <= visibleBeforeUtc).ToList();
        var hasMore = visible.Count > limit || visible.Count < rows.Count;
        if (visible.Count > limit) visible = visible.Take(limit).ToList();

        var instanceIds = visible
            .Select(r => ResolveInstanceId(r.Event.Metadata, r.Event.CorrelationId))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var instances = instanceIds.Count == 0
            ? new Dictionary<Guid, InstanceInfo>()
            : await _db.WorkflowInstances
                .AsNoTracking()
                .Where(w => w.TenantId == tenantId && instanceIds.Contains(w.Id))
                .Select(w => new InstanceInfo(w.Id, w.WorkflowDefinitionId, w.WorkflowVersion, w.ExternalSourceSystem, w.ExternalReference))
                .ToDictionaryAsync(w => w.Id, cancellationToken);

        var items = new List<ChangeFeedItem>(visible.Count);
        foreach (var row in visible)
        {
            var e = row.Event;
            var metadata = e.Metadata ?? new Dictionary<string, string>();
            var candidateId = ResolveInstanceId(metadata, e.CorrelationId);
            InstanceInfo? instance = candidateId.HasValue && instances.TryGetValue(candidateId.Value, out var found) ? found : null;
            metadata.TryGetValue(PayloadKey, out var payload);

            items.Add(new ChangeFeedItem(
                row.Sequence,
                e.EventId,
                e.EventType,
                e.Version,
                DateTime.SpecifyKind(e.Timestamp, DateTimeKind.Utc),
                e.CorrelationId,
                ParseGuid(metadata, "CausationId"),
                instance?.Id,
                instance?.DefinitionId,
                instance?.Version,
                instance?.SourceSystem,
                instance?.Reference,
                payload,
                metadata.Where(kv => kv.Key != PayloadKey).ToDictionary(kv => kv.Key, kv => kv.Value)));
        }

        var last = items.Count > 0 ? items[^1].Sequence : afterSequence;
        return new ChangeFeedPage(items, last, hasMore);
    }

    private static Guid? ResolveInstanceId(IReadOnlyDictionary<string, string>? metadata, Guid? correlationId)
        => (metadata != null ? ParseGuid(metadata, "WorkflowInstanceId") : null) ?? correlationId;

    private static Guid? ParseGuid(IReadOnlyDictionary<string, string> metadata, string key)
        => metadata.TryGetValue(key, out var raw) && Guid.TryParse(raw, out var id) ? id : null;

    private sealed record InstanceInfo(Guid Id, Guid DefinitionId, int Version, string? SourceSystem, string? Reference);
}
