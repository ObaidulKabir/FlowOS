using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Core.Common.Models; // Changed namespace
using FlowOS.Events.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
// using FlowOS.Infrastructure.Persistence; // REMOVED

namespace FlowOS.Notifications.Infrastructure.Persistence;

public class EventPublishingInterceptor : SaveChangesInterceptor
{
    private readonly IPublisher _publisher;
    private List<DomainEvent> _eventsToPublish = new();

    public EventPublishingInterceptor(IPublisher publisher)
    {
        _publisher = publisher;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, 
        InterceptionResult<int> result, 
        CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;
        if (context == null) return await base.SavingChangesAsync(eventData, result, cancellationToken);

        // We depend on ChangeTracker, which is available in DbContext base class.
        // We do NOT need to cast to FlowOSDbContext.
        // But we DO need to know about 'DomainEvent' class which is in FlowOS.Events.
        // That is fine, FlowOS.Notifications refs FlowOS.Events.

        _eventsToPublish = context.ChangeTracker.Entries<DomainEvent>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .ToList();

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, 
        int result, 
        CancellationToken cancellationToken = default)
    {
        if (_eventsToPublish.Any())
        {
            foreach (var domainEvent in _eventsToPublish)
            {
                await _publisher.Publish(new DomainEventNotification<DomainEvent>(domainEvent), cancellationToken);
            }
            _eventsToPublish.Clear();
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }
}
