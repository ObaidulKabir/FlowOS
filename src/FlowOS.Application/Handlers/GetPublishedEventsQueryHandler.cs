using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Application.Queries;
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
        var events = await _unitOfWork.Events.ListByTenantAsync(
            request.TenantId,
            request.WorkflowInstanceId,
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
}
