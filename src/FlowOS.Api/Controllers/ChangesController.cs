using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Api.Http;
using FlowOS.Application.Common;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace FlowOS.API.Controllers;

/// <summary>
/// CR-10c: pull change feed. Returns the caller's tenant events in storage order, after a cursor.
/// Schema: docs/schemas/change-feed.v1.schema.json.
/// </summary>
[ApiController]
[Route("api/changes")]
[Authorize]
public class ChangesController : ControllerBase
{
    public const string SchemaId = "flowos.change-feed/v1";
    public const int DefaultLimit = 100;
    public const int MaxLimit = 500;
    public const int DefaultSettleSeconds = 5;

    private readonly IChangeFeedReader _reader;
    private readonly ICurrentUser _currentUser;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;

    public ChangesController(IChangeFeedReader reader, ICurrentUser currentUser, IConfiguration configuration, TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _reader = reader;
        _currentUser = currentUser;
        _configuration = configuration;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? after, [FromQuery] int limit = DefaultLimit, CancellationToken cancellationToken = default)
    {
        if (_currentUser.TenantId == Guid.Empty)
            return Unauthorized("TenantId is missing.");
        if (!ChangeFeedCursor.TryDecode(after, out var afterSequence))
            return BadRequest(new ProblemDetails { Title = "Invalid cursor", Detail = "Send back a nextCursor value exactly as FlowOS returned it.", Status = 400 });
        if (limit < 1 || limit > MaxLimit)
            return BadRequest(new ProblemDetails { Title = "Invalid limit", Detail = $"limit must be between 1 and {MaxLimit}.", Status = 400 });

        var settleSeconds = _configuration.GetValue("FlowOS:ChangeFeed:SettleSeconds", DefaultSettleSeconds);
        var now = _timeProvider.GetUtcNow();
        var page = await _reader.ReadAsync(_currentUser.TenantId, afterSequence, limit, now.UtcDateTime.AddSeconds(-Math.Max(0, settleSeconds)), cancellationToken);

        var body = Serialize(page);
        var signingKey = _configuration["FlowOS:ChangeFeed:SigningKey"];
        if (!string.IsNullOrEmpty(signingKey))
        {
            Response.Headers[ChangeFeedSignature.HeaderName] =
                ChangeFeedSignature.Compute(Encoding.UTF8.GetBytes(signingKey), now.ToUnixTimeSeconds(), body);
        }

        return File(body, "application/json");
    }

    public static byte[] Serialize(ChangeFeedPage page)
    {
        using var stream = new System.IO.MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("schema", SchemaId);
            w.WriteStartArray("items");
            foreach (var item in page.Items)
            {
                w.WriteStartObject();
                w.WriteNumber("sequence", item.Sequence);
                w.WriteString("eventId", item.EventId);
                w.WriteString("eventType", item.EventType);
                w.WriteNumber("eventVersion", item.EventVersion);
                w.WriteString("occurredAt", item.OccurredAtUtc);
                WriteGuid(w, "correlationId", item.CorrelationId);
                WriteGuid(w, "causationId", item.CausationId);
                WriteGuid(w, "workflowInstanceId", item.WorkflowInstanceId);
                WriteGuid(w, "workflowDefinitionId", item.WorkflowDefinitionId);
                if (item.WorkflowVersion.HasValue) w.WriteNumber("workflowVersion", item.WorkflowVersion.Value); else w.WriteNull("workflowVersion");
                w.WriteString("externalSourceSystem", item.ExternalSourceSystem);
                w.WriteString("externalReference", item.ExternalReference);
                w.WritePropertyName("payload");
                WritePayload(w, item.PayloadJson);
                w.WriteStartObject("metadata");
                foreach (var kv in item.Metadata) w.WriteString(kv.Key, kv.Value);
                w.WriteEndObject();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteString("nextCursor", ChangeFeedCursor.Encode(page.LastSequence));
            w.WriteBoolean("hasMore", page.HasMore);
            w.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static void WriteGuid(Utf8JsonWriter w, string name, Guid? value)
    {
        if (value.HasValue) w.WriteString(name, value.Value); else w.WriteNull(name);
    }

    private static void WritePayload(Utf8JsonWriter w, string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { w.WriteNullValue(); return; }
        try
        {
            using var doc = JsonDocument.Parse(json);
            doc.RootElement.WriteTo(w);
        }
        catch (JsonException)
        {
            w.WriteStringValue(json); // stored payload that is not JSON is passed through as text
        }
    }
}
