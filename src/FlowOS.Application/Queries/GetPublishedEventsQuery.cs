using System;
using System.Collections.Generic;
using MediatR;

namespace FlowOS.Application.Queries;

public record GetPublishedEventsQuery(Guid TenantId, Guid? WorkflowInstanceId = null, int Limit = 50) : IRequest<List<PublishedEventDto>>;

public class PublishedEventDto
{
    public Guid EventId { get; set; }
    public Guid TenantId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public Guid? CorrelationId { get; set; }
    public DateTime Timestamp { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
    public string? PayloadJson { get; set; }
}
