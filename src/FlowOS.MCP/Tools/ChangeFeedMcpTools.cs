using System;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Application.Common;
using FlowOS.Application.Common.Interfaces;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Tools;

/// <summary>
/// CR-10c: read-only view of the tenant change feed (GET /api/changes) for agents, for example to
/// answer "what happened since this cursor" while supporting an integration. Same cursor, order and
/// settle window as the HTTP API; integrating systems should keep using the signed HTTP endpoint.
/// </summary>
public sealed class ChangeFeedMcpTools
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 500;
    public const int DefaultSettleSeconds = 5;

    private readonly IChangeFeedReader _reader;
    private readonly IConfiguration? _configuration;

    public ChangeFeedMcpTools(IChangeFeedReader reader, IConfiguration? configuration = null)
    {
        _reader = reader;
        _configuration = configuration;
    }

    public async Task<CallToolResult> ReadChangeFeed(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);

            var after = args["after"]?.ToString();
            if (!ChangeFeedCursor.TryDecode(after, out var afterSequence))
                return McpToolResults.Fail("MCP-ARG-002", "after must be a nextCursor value exactly as FlowOS returned it.");

            var limit = DefaultLimit;
            if (args["limit"] != null)
            {
                if (args["limit"]!.Type != JTokenType.Integer || args["limit"]!.Value<int>() is < 1 or > MaxLimit)
                    return McpToolResults.Fail("MCP-ARG-002", $"limit must be an integer between 1 and {MaxLimit}.");
                limit = args["limit"]!.Value<int>();
            }

            var settleSeconds = DefaultSettleSeconds;
            if (int.TryParse(_configuration?["FlowOS:ChangeFeed:SettleSeconds"], out var configured)) settleSeconds = Math.Max(0, configured);

            var page = await _reader.ReadAsync(tenantId, afterSequence, limit, DateTime.UtcNow.AddSeconds(-settleSeconds));

            return McpToolResults.Success(new
            {
                schema = "flowos.change-feed/v1",
                items = page.Items.Select(i => new
                {
                    sequence = i.Sequence,
                    eventId = i.EventId,
                    eventType = i.EventType,
                    eventVersion = i.EventVersion,
                    occurredAt = i.OccurredAtUtc,
                    correlationId = i.CorrelationId,
                    causationId = i.CausationId,
                    workflowInstanceId = i.WorkflowInstanceId,
                    workflowDefinitionId = i.WorkflowDefinitionId,
                    workflowVersion = i.WorkflowVersion,
                    externalSourceSystem = i.ExternalSourceSystem,
                    externalReference = i.ExternalReference,
                    payload = ParsePayload(i.PayloadJson),
                    metadata = i.Metadata
                }).ToList(),
                nextCursor = ChangeFeedCursor.Encode(page.LastSequence),
                hasMore = page.HasMore
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to read the change feed: {ex.Message}");
        }
    }

    private static JToken? ParsePayload(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JToken.Parse(json);
        }
        catch (Newtonsoft.Json.JsonReaderException)
        {
            return new JValue(json);
        }
    }
}
