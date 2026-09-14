using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Core.Common.Interfaces;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using FlowOS.Workflows.Domain;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Tools;

public sealed record ActionPluginParameter(
    [property: Newtonsoft.Json.JsonProperty("name")] string Name,
    [property: Newtonsoft.Json.JsonProperty("type")] string Type,
    [property: Newtonsoft.Json.JsonProperty("required")] bool Required,
    [property: Newtonsoft.Json.JsonProperty("description")] string Description);

public sealed record ActionPluginCapabilityMetadata(
    [property: Newtonsoft.Json.JsonProperty("category")] string Category,
    [property: Newtonsoft.Json.JsonProperty("description")] string Description,
    [property: Newtonsoft.Json.JsonProperty("supportedParameters")] IReadOnlyList<ActionPluginParameter> SupportedParameters,
    [property: Newtonsoft.Json.JsonProperty("examplePayloadMapping")] IReadOnlyDictionary<string, object> ExamplePayloadMapping);

public class PluginDiscoveryMcpTools
{
    private readonly IEnumerable<IWorkflowActionPlugin> _actionPlugins;
    private readonly IEnumerable<IPolicyDecisionPlugin> _decisionPlugins;
    private readonly IConfiguration _configuration;

    public PluginDiscoveryMcpTools(
        IEnumerable<IWorkflowActionPlugin> actionPlugins,
        IEnumerable<IPolicyDecisionPlugin> decisionPlugins,
        IConfiguration configuration)
    {
        _actionPlugins = actionPlugins ?? Enumerable.Empty<IWorkflowActionPlugin>();
        _decisionPlugins = decisionPlugins ?? Enumerable.Empty<IPolicyDecisionPlugin>();
        _configuration = configuration;
    }

    public Task<CallToolResult> ListRegisteredPlugins(JObject args)
    {
        try
        {
            var includeWildcard = args["includeWildcard"]?.Value<bool>() ?? true;

            var actionPlugins = _actionPlugins
                .Select(p => p.ActionType?.Trim())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(p => includeWildcard || !string.Equals(p, "*", StringComparison.Ordinal))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Select(p =>
                {
                    var meta = GetActionPluginMetadata(p);
                    return new
                    {
                        actionType = p,
                        isWildcard = string.Equals(p, "*", StringComparison.Ordinal),
                        kind = IsBuiltInActionType(p) ? "built-in" : "custom",
                        category = meta.Category,
                        description = meta.Description,
                        supportedParameters = meta.SupportedParameters,
                        examplePayloadMapping = meta.ExamplePayloadMapping
                    };
                })
                .ToList();

            var decisionPlugins = _decisionPlugins
                .Select(p => p.ProviderName?.Trim())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Select(p => new
                {
                    providerName = p,
                    kind = string.Equals(p, "default", StringComparison.OrdinalIgnoreCase) ? "built-in" : "custom"
                })
                .ToList();

            var allowWildcardFallback = !bool.TryParse(
                _configuration["FlowOS:Actions:AllowWildcardPluginFallback"],
                out var wildcardSetting) || wildcardSetting;

            var rejectUnknownActionTypes = bool.TryParse(
                _configuration["FlowOS:Actions:RejectUnknownActionTypes"],
                out var rejectUnknown) && rejectUnknown;

            return Task.FromResult(McpToolResults.Success(new
            {
                totalActionPlugins = actionPlugins.Count,
                totalDecisionPlugins = decisionPlugins.Count,
                actionPlugins,
                decisionPlugins,
                conventions = new
                {
                    customActionAliasPrefixes = new[] { "plugin:", "plugin." },
                    decisionProviderField = "workflow.steps[].decisionProvider"
                },
                runtimePolicy = new
                {
                    allowWildcardPluginFallback = allowWildcardFallback,
                    rejectUnknownActionTypes
                }
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResults.Fail(
                "MCP-INTERNAL",
                $"Failed to list registered plugins: {ex.Message}"));
        }
    }

    public Task<CallToolResult> TestActionPlugin(JObject args)
    {
        try
        {
            var actionType = args["actionType"]?.Value<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(actionType))
            {
                return Task.FromResult(McpToolResults.Fail("MCP-ARG-001", "Field 'actionType' is required."));
            }

            var plugin = _actionPlugins.FirstOrDefault(p =>
                string.Equals(p.ActionType?.Trim(), actionType, StringComparison.OrdinalIgnoreCase))
                ?? _actionPlugins.FirstOrDefault(p => string.Equals(p.ActionType?.Trim(), "*", StringComparison.Ordinal));

            if (plugin == null)
            {
                return Task.FromResult(McpToolResults.Fail(
                    "MCP-NOTFOUND-001",
                    $"Action plugin '{actionType}' is not registered on the server runtime."));
            }

            var target = args["target"]?.Value<string>()?.Trim();
            var template = args["template"]?.Value<string>()?.Trim();
            var url = args["url"]?.Value<string>()?.Trim();
            var payloadObj = args["payload"] as JObject;

            Guid tenantId = Guid.Empty;
            if (args["tenantId"] != null && Guid.TryParse(args["tenantId"]!.Value<string>(), out var parsedTenantId))
            {
                tenantId = parsedTenantId;
            }
            else if (McpRequestContext.TenantId != Guid.Empty)
            {
                tenantId = McpRequestContext.TenantId;
            }
            else
            {
                tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            }

            var resolvedActionPayload = ConvertJObjectToDictionary(payloadObj);
            var runtimePayload = new Dictionary<string, object>(resolvedActionPayload, StringComparer.OrdinalIgnoreCase);

            var actionDef = new StepActionDefinition(actionType)
            {
                Target = target,
                Template = template,
                Url = url
            };

            var context = new WorkflowActionPluginContext(
                TenantId: tenantId,
                WorkflowInstanceId: Guid.NewGuid(),
                StepId: "TestStep",
                TriggerPhase: "OnExit",
                Action: actionDef,
                RuntimePayload: runtimePayload,
                ResolvedTarget: target,
                ResolvedCapability: null,
                ResolvedUrl: url,
                ResolvedTemplate: template,
                ResolvedHeaders: null,
                ResolvedActionPayload: resolvedActionPayload);

            var result = plugin.BuildMessage(context);

            var diagnostics = RunPluginDiagnostics(actionType, target, template, url, result.MessagePayload);
            var status = diagnostics.Count == 0 ? "Valid" : "Warning";

            return Task.FromResult(McpToolResults.Success(new
            {
                actionType = plugin.ActionType,
                messageType = result.MessageType,
                isRegistered = true,
                status,
                builtPayload = result.MessagePayload,
                diagnostics
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(McpToolResults.Fail(
                "MCP-INTERNAL",
                $"Failed to test action plugin: {ex.Message}"));
        }
    }

    private static ActionPluginCapabilityMetadata GetActionPluginMetadata(string? actionType)
    {
        return (actionType?.Trim().ToLowerInvariant()) switch
        {
            "email" => new ActionPluginCapabilityMetadata(
                Category: "communication",
                Description: "Dispatches transactional emails with HTML/plain-text formatting, CC/BCC, and outbox durability.",
                SupportedParameters: new[]
                {
                    new ActionPluginParameter("to", "string", true, "Recipient email address (can also be mapped via action.target)."),
                    new ActionPluginParameter("subject", "string", true, "Email subject line (falls back to action.template)."),
                    new ActionPluginParameter("body", "string", false, "Plain-text email body content."),
                    new ActionPluginParameter("htmlBody", "string", false, "Optional HTML formatted email body."),
                    new ActionPluginParameter("cc", "string", false, "Optional CC recipient(s)."),
                    new ActionPluginParameter("bcc", "string", false, "Optional BCC recipient(s).")
                },
                ExamplePayloadMapping: new Dictionary<string, object>
                {
                    ["to"] = "dev-ops@company.com",
                    ["subject"] = "Deployment Completed: {{version}}",
                    ["body"] = "Workflow instance {{instanceId}} completed step {{stepId}}."
                }),

            "slack" => new ActionPluginCapabilityMetadata(
                Category: "communication",
                Description: "Sends real-time messages, Block Kit layouts, or attachments to Slack channels or incoming webhooks.",
                SupportedParameters: new[]
                {
                    new ActionPluginParameter("channel", "string", false, "Slack channel name (e.g. #alerts, defaults to #general or action.target)."),
                    new ActionPluginParameter("text", "string", true, "Message text or markdown summary (falls back to action.template)."),
                    new ActionPluginParameter("webhookUrl", "string", false, "Incoming webhook URL override (falls back to action.url)."),
                    new ActionPluginParameter("blocks", "array", false, "Slack Block Kit layout blocks for rich interactive formatting."),
                    new ActionPluginParameter("attachments", "array", false, "Secondary Slack attachment objects.")
                },
                ExamplePayloadMapping: new Dictionary<string, object>
                {
                    ["channel"] = "#workflow-alerts",
                    ["text"] = "High priority task requires review: {{taskId}}",
                    ["webhookUrl"] = "https://hooks.slack.com/services/T00/B00/XXXX"
                }),

            "whatsapp" => new ActionPluginCapabilityMetadata(
                Category: "communication",
                Description: "Sends WhatsApp transactional notifications or HSM templates via Meta/Twilio business messaging.",
                SupportedParameters: new[]
                {
                    new ActionPluginParameter("recipient", "string", true, "Recipient phone number in E.164 format (e.g. +14155552671, falls back to action.target)."),
                    new ActionPluginParameter("templateName", "string", false, "Approved HSM template name (falls back to single-word action.template)."),
                    new ActionPluginParameter("message", "string", false, "Direct text message for active customer session windows."),
                    new ActionPluginParameter("language", "string", false, "Template language code (e.g. en_US, defaults to en_US)."),
                    new ActionPluginParameter("parameters", "object|array", false, "Template variable parameters or components."),
                    new ActionPluginParameter("mediaUrl", "string", false, "Optional media attachment URL (image/pdf/document).")
                },
                ExamplePayloadMapping: new Dictionary<string, object>
                {
                    ["recipient"] = "+14155552671",
                    ["templateName"] = "order_status_update",
                    ["language"] = "en_US",
                    ["parameters"] = new Dictionary<string, string> { ["1"] = "Confirmed", ["2"] = "Order #1234" }
                }),

            "webhook" => new ActionPluginCapabilityMetadata(
                Category: "integration",
                Description: "Sends authenticated HTTP requests with HMAC signatures, idempotency headers, and payload mapping.",
                SupportedParameters: new[]
                {
                    new ActionPluginParameter("url", "string", true, "Target webhook endpoint URL (falls back to action.url)."),
                    new ActionPluginParameter("method", "string", false, "HTTP method (GET, POST, PUT, DELETE, defaults to POST)."),
                    new ActionPluginParameter("headers", "object", false, "Custom HTTP headers dictionary."),
                    new ActionPluginParameter("body", "object", false, "Structured HTTP request body payload.")
                },
                ExamplePayloadMapping: new Dictionary<string, object>
                {
                    ["url"] = "https://api.example.com/webhooks/workflow",
                    ["method"] = "POST"
                }),

            "notification" => new ActionPluginCapabilityMetadata(
                Category: "internal",
                Description: "Creates internal FlowOS notifications for users and tenants visible via notification feed tools.",
                SupportedParameters: new[]
                {
                    new ActionPluginParameter("recipient", "string", true, "Target user or role ID (falls back to action.target)."),
                    new ActionPluginParameter("message", "string", true, "Notification message content (falls back to action.template).")
                },
                ExamplePayloadMapping: new Dictionary<string, object>
                {
                    ["recipient"] = "admin@company.com",
                    ["message"] = "Order #{{orderId}} requires manual escalation."
                }),

            "publishevent" => new ActionPluginCapabilityMetadata(
                Category: "internal",
                Description: "Emits domain events onto the FlowOS message bus to trigger reactive workflows and subscribers.",
                SupportedParameters: new[]
                {
                    new ActionPluginParameter("eventName", "string", true, "Domain event type name (falls back to action.target)."),
                    new ActionPluginParameter("payload", "object", false, "Event data dictionary.")
                },
                ExamplePayloadMapping: new Dictionary<string, object>
                {
                    ["eventName"] = "WorkflowStepCompleted",
                    ["payload"] = new Dictionary<string, string> { ["status"] = "Success" }
                }),

            "invokecapability" => new ActionPluginCapabilityMetadata(
                Category: "internal",
                Description: "Invokes registered capability bindings with dynamic input parameter resolution.",
                SupportedParameters: new[]
                {
                    new ActionPluginParameter("capabilityName", "string", true, "Registered capability name (falls back to action.capability)."),
                    new ActionPluginParameter("input", "object", false, "Input arguments passed to the capability service.")
                },
                ExamplePayloadMapping: new Dictionary<string, object>
                {
                    ["capabilityName"] = "CreditCheckService",
                    ["input"] = new Dictionary<string, object> { ["minScore"] = 700 }
                }),

            "*" => new ActionPluginCapabilityMetadata(
                Category: "fallback",
                Description: "Dynamic fallback plugin handler for wildcard and tenant-aliased plugins.",
                SupportedParameters: Array.Empty<ActionPluginParameter>(),
                ExamplePayloadMapping: new Dictionary<string, object>()),

            _ => new ActionPluginCapabilityMetadata(
                Category: "custom",
                Description: $"Custom action plugin provider for '{actionType}'.",
                SupportedParameters: Array.Empty<ActionPluginParameter>(),
                ExamplePayloadMapping: new Dictionary<string, object>())
        };
    }

    private static List<string> RunPluginDiagnostics(
        string actionType,
        string? target,
        string? template,
        string? url,
        object messagePayload)
    {
        var diagnostics = new List<string>();
        var payloadDict = messagePayload as IDictionary<string, object>;

        switch (actionType.ToLowerInvariant())
        {
            case "email":
                var to = payloadDict != null && payloadDict.TryGetValue("to", out var toVal)
                    ? toVal?.ToString()
                    : target;
                if (string.IsNullOrWhiteSpace(to))
                {
                    diagnostics.Add("Email recipient ('to' or 'target') is missing.");
                }
                else if (!to.Contains('@'))
                {
                    diagnostics.Add($"Email recipient '{to}' does not contain an '@' sign.");
                }

                var subject = payloadDict != null && payloadDict.TryGetValue("subject", out var subjVal)
                    ? subjVal?.ToString()
                    : template;
                if (string.IsNullOrWhiteSpace(subject))
                {
                    diagnostics.Add("Email subject is empty; consider supplying 'subject' in payload or 'template'.");
                }
                break;

            case "slack":
                var text = payloadDict != null && payloadDict.TryGetValue("text", out var textVal)
                    ? textVal?.ToString()
                    : template;
                var blocks = payloadDict != null && payloadDict.TryGetValue("blocks", out var blocksVal)
                    ? blocksVal
                    : null;
                if (string.IsNullOrWhiteSpace(text) && blocks == null)
                {
                    diagnostics.Add("Slack message content ('text' or 'blocks') is missing.");
                }
                break;

            case "whatsapp":
                var recipient = payloadDict != null && payloadDict.TryGetValue("recipient", out var recVal)
                    ? recVal?.ToString()
                    : target;
                if (string.IsNullOrWhiteSpace(recipient))
                {
                    diagnostics.Add("WhatsApp recipient ('recipient' or 'target') is missing.");
                }
                else if (!recipient.StartsWith("+") && !recipient.All(char.IsDigit))
                {
                    diagnostics.Add("WhatsApp recipient should be in E.164 international format (e.g. +14155552671).");
                }

                var templateName = payloadDict != null && payloadDict.TryGetValue("templateName", out var tmplVal)
                    ? tmplVal?.ToString()
                    : null;
                var message = payloadDict != null && payloadDict.TryGetValue("message", out var msgVal)
                    ? msgVal?.ToString()
                    : null;
                if (string.IsNullOrWhiteSpace(templateName) && string.IsNullOrWhiteSpace(message) && string.IsNullOrWhiteSpace(template))
                {
                    diagnostics.Add("WhatsApp requires either 'templateName' or 'message' text.");
                }
                break;

            case "webhook":
                var whUrl = payloadDict != null && payloadDict.TryGetValue("url", out var urlVal)
                    ? urlVal?.ToString()
                    : url;
                if (string.IsNullOrWhiteSpace(whUrl))
                {
                    diagnostics.Add("Webhook endpoint URL ('url') is missing.");
                }
                else if (!Uri.TryCreate(whUrl, UriKind.Absolute, out _))
                {
                    diagnostics.Add($"Webhook URL '{whUrl}' is not a valid absolute URI.");
                }
                break;
        }

        return diagnostics;
    }

    private static Dictionary<string, object> ConvertJObjectToDictionary(JObject? jObj)
    {
        var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (jObj == null) return dict;

        foreach (var prop in jObj.Properties())
        {
            dict[prop.Name] = ConvertJToken(prop.Value);
        }
        return dict;
    }

    private static object ConvertJToken(JToken token)
    {
        return token.Type switch
        {
            JTokenType.Object => ConvertJObjectToDictionary((JObject)token),
            JTokenType.Array => ((JArray)token).Select(ConvertJToken).ToList(),
            JTokenType.Integer => token.Value<long>(),
            JTokenType.Float => token.Value<double>(),
            JTokenType.String => token.Value<string>() ?? string.Empty,
            JTokenType.Boolean => token.Value<bool>(),
            JTokenType.Null => string.Empty,
            _ => token.ToString()
        };
    }

    private static bool IsBuiltInActionType(string? actionType)
    {
        if (string.IsNullOrWhiteSpace(actionType)) return false;

        return string.Equals(actionType, "Webhook", StringComparison.OrdinalIgnoreCase)
            || string.Equals(actionType, "Notification", StringComparison.OrdinalIgnoreCase)
            || string.Equals(actionType, "PublishEvent", StringComparison.OrdinalIgnoreCase)
            || string.Equals(actionType, "InvokeCapability", StringComparison.OrdinalIgnoreCase)
            || string.Equals(actionType, "Slack", StringComparison.OrdinalIgnoreCase)
            || string.Equals(actionType, "Email", StringComparison.OrdinalIgnoreCase)
            || string.Equals(actionType, "WhatsApp", StringComparison.OrdinalIgnoreCase)
            || string.Equals(actionType, "*", StringComparison.Ordinal);
    }
}
