using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Core.Common.Models;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Entities.ExternalAI;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FlowOS.Notifications.Infrastructure.Persistence;

public class ExternalAgentChangeInterceptor : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;
        if (context == null)
            return await base.SavingChangesAsync(eventData, result, cancellationToken);

        var addedOutboxes = context.ChangeTracker.Entries<OutboxMessage>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .ToList();

        if (addedOutboxes.Count == 0)
            return await base.SavingChangesAsync(eventData, result, cancellationToken);

        foreach (var outbox in addedOutboxes)
        {
            var tenant = await context.Set<Tenant>().FindAsync([outbox.TenantId], cancellationToken);
            if (tenant?.ExternalAIAgentEnabled != true)
                continue;

            var alreadyTracked = context.ChangeTracker.Entries<ExternalAgentChangeRecord>()
                .Any(e => e.Entity.SourceOutboxMessageId == outbox.Id);
            if (alreadyTracked)
                continue;

            var alreadyPersisted = await context.Set<ExternalAgentChangeRecord>()
                .AnyAsync(x => x.SourceOutboxMessageId == outbox.Id, cancellationToken);
            if (alreadyPersisted)
                continue;

            context.Set<ExternalAgentChangeRecord>().Add(
                ExternalAgentChangeRecord.FromOutboxMessage(
                    Guid.NewGuid(),
                    outbox.TenantId,
                    outbox.Id,
                    outbox.Type,
                    outbox.Payload,
                    outbox.OccurredOnUtc));
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
