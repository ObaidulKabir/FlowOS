using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Application.Queries;
using FlowOS.Events.Models;
using MediatR;

namespace FlowOS.Application.Handlers;

public class GetPublishedEventsQueryHandler : IRequestHandler<GetPublishedEventsQuery, List<PublishedEventDto>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetPublishedEventsQueryHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<List<PublishedEventDto>> Handle(GetPublishedEventsQuery request, CancellationToken cancellationToken)
    {
        var events = request.WorkflowInstanceId.HasValue && request.WorkflowInstanceId.Value != Guid.Empty
            ? await LoadInstanceEventsAsync(request.TenantId, request.WorkflowInstanceId.Value, request.Limit, cancellationToken)
            : await _unitOfWork.Events.ListByTenantAsync(
                request.TenantId,
                null,
                request.Limit,
                cancellationToken);

        return events.Select(e =>
        {
            e.Metadata.TryGetValue("Payload", out var payloadJson);

            return new PublishedEventDto
            {
                EventId = e.EventId,
                TenantId = e.TenantId,
                EventType = e.EventType,
                CorrelationId = e.CorrelationId,
                Timestamp = e.Timestamp,
                Metadata = e.Metadata != null ? new Dictionary<string, string>(e.Metadata) : new Dictionary<string, string>(),
                PayloadJson = payloadJson
            };
        }).ToList();
    }

    private async Task<List<DomainEvent>> LoadInstanceEventsAsync(
        Guid tenantId,
        Guid instanceOrCorrelationId,
        int limit,
        CancellationToken cancellationToken)
    {
        var correlationIds = new HashSet<Guid> { instanceOrCorrelationId };
        var instance = await _unitOfWork.WorkflowInstances
            .GetByIdAsNoTrackingAsync(instanceOrCorrelationId, tenantId, cancellationToken);

        if (instance != null)
        {
            correlationIds.Add(instance.Id);
            if (instance.CorrelationId.HasValue && instance.CorrelationId.Value != Guid.Empty)
            {
                correlationIds.Add(instance.CorrelationId.Value);
            }
        }

        var merged = new Dictionary<Guid, DomainEvent>();
        foreach (var correlationId in correlationIds)
        {
            var batch = await _unitOfWork.Events.ListByCorrelationIdAsync(correlationId, cancellationToken);
            foreach (var evt in batch.Where(e => e.TenantId == tenantId))
            {
                merged[evt.EventId] = evt;
            }
        }

        return merged.Values
            .OrderByDescending(evt => evt.Timestamp)
            .ThenByDescending(evt => evt.EventId)
            .Take(limit > 0 ? limit : 50)
            .ToList();
    }
}
