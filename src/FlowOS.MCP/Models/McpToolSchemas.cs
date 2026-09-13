using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Models;

public static class McpToolSchemas
{
    public static JObject NoArguments() => JObject.Parse(
        """{"type":"object","properties":{},"additionalProperties":false}""");

    public static JObject TenantOptional() => JObject.Parse(
        """{"type":"object","properties":{"tenantId":{"type":"string","format":"uuid"}},"additionalProperties":false}""");

    public static JObject ListNotifications() => JObject.Parse(
        """
        {
          "type":"object",
          "properties":{
            "tenantId":{"type":"string","format":"uuid"},
            "userId":{"type":"string","format":"uuid"}
          },
          "additionalProperties":false
        }
        """);

    public static JObject MarkNotificationAsRead() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["id"],
          "properties":{
            "id":{"type":"string","format":"uuid"},
            "tenantId":{"type":"string","format":"uuid"},
            "userId":{"type":"string","format":"uuid"}
          },
          "additionalProperties":false
        }
        """);

    public static JObject SuggestAgentAction() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["workflowInstanceId","agentId"],
          "properties":{
            "workflowInstanceId":{"type":"string","format":"uuid"},
            "agentId":{"type":"string","enum":["RiskAnalysisAgent"]},
            "tenantId":{"type":"string","format":"uuid"},
            "objective":{"type":"string","maxLength":500}
          },
          "additionalProperties":false
        }
        """);

    public static JObject ExplainValidationViolation() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["code"],
          "properties":{
            "code":{"type":"string","minLength":1},
            "context":{"type":"object","additionalProperties":true}
          },
          "additionalProperties":false
        }
        """);

    public static JObject DraftById(string idProperty = "id") => JObject.Parse(
        $$"""
        {
          "type":"object",
          "required":["{{idProperty}}"],
          "properties":{
            "{{idProperty}}":{"type":"string","format":"uuid"},
            "tenantId":{"type":"string","format":"uuid"}
          },
          "additionalProperties":false
        }
        """);

    public static JObject PublishWorkflowClass() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["id"],
          "properties":{
            "id":{"type":"string","format":"uuid"},
            "tenantId":{"type":"string","format":"uuid"},
            "confirmHumanApproval":{
              "type":"boolean",
              "description":"Explicit confirmation of human approval for high-risk irreversible publication."
            }
          },
          "additionalProperties":false
        }
        """);

    public static JObject WorkflowInstanceStatus() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["instanceId"],
          "properties":{
            "instanceId":{"type":"string","format":"uuid"},
            "tenantId":{"type":"string","format":"uuid"}
          },
          "additionalProperties":false
        }
        """);

    public static JObject CreateDraft() => JObject.Parse(
        $$"""
        {
          "type":"object",
          "required":["name","blueprint"],
          "properties":{
            "name":{"type":"string","minLength":1,"maxLength":200},
            "version":{"type":"string","default":"1.0.0","pattern":"^\\d+\\.\\d+\\.\\d+$"},
            "tenantId":{"type":"string","format":"uuid"},
            "blueprint":{{BlueprintSchema().ToString(Newtonsoft.Json.Formatting.None)}}
          },
          "additionalProperties":false
        }
        """);

    public static JObject UpdateDraft() => JObject.Parse(
        $$"""
        {
          "type":"object",
          "required":["id","blueprint"],
          "properties":{
            "id":{"type":"string","format":"uuid"},
            "tenantId":{"type":"string","format":"uuid"},
            "name":{"type":"string","minLength":1,"maxLength":200},
            "version":{"type":"string","pattern":"^\\d+\\.\\d+\\.\\d+$"},
            "blueprint":{{BlueprintSchema().ToString(Newtonsoft.Json.Formatting.None)}}
          },
          "additionalProperties":false
        }
        """);

    public static JObject BlueprintSchema() => JObject.Parse(
        """
        {
          "$schema":"https://json-schema.org/draft/2020-12/schema",
          "type":"object",
          "required":["events","stateMachine","workflow","roles","capabilities"],
          "properties":{
            "events":{
              "type":"array",
              "items":{
                "type":"object",
                "required":["eventId"],
                "properties":{
                  "eventId":{"type":"string","minLength":1},
                  "name":{"type":"string"},
                  "description":{"type":"string"},
                  "category":{"type":"string","enum":["Decision","System","Human","Agent"],"default":"System"},
                  "isTerminal":{"type":"boolean","default":false},
                  "payloadSchema":{"type":["string","null"]}
                },
                "additionalProperties":false
              }
            },
            "stateMachine":{
              "type":"object",
              "required":["initialState","states","transitions"],
              "properties":{
                "entityType":{"type":"string"},
                "initialState":{"type":"string","minLength":1},
                "states":{"type":"array","minItems":1,"items":{"type":"string"}},
                "transitions":{
                  "type":"array",
                  "items":{
                    "type":"object",
                    "required":["fromState","toState","eventId"],
                    "properties":{
                      "fromState":{"type":"string"},
                      "toState":{"type":"string"},
                      "eventId":{"type":"string"},
                      "constraints":{"type":"object","additionalProperties":{"type":"string"}}
                    },
                    "additionalProperties":false
                  }
                }
              },
              "additionalProperties":false
            },
            "workflow":{
              "type":"object",
              "required":["startStepId","steps"],
              "properties":{
                "startStepId":{"type":"string","minLength":1},
                "steps":{
                  "type":"array",
                  "minItems":1,
                  "items":{
                    "type":"object",
                    "required":["stepId","stepType"],
                    "properties":{
                      "stepId":{"type":"string","minLength":1},
                      "stepType":{"type":"string","enum":["Command","SystemTask","HumanTask","Timer","Decision","End"]},
                      "nextSteps":{"type":"object","additionalProperties":{"type":"string"}},
                      "requiredRoles":{"type":"array","items":{"type":"string"}},
                      "conditions":{"type":"object","additionalProperties":{"type":"string"}},
                      "sla":{
                        "type":"object",
                        "required":["duration","timeoutEvent"],
                        "properties":{
                          "duration":{"type":"string","minLength":1},
                          "timeoutEvent":{"type":"string","minLength":1},
                          "escalationStepId":{"type":"string"},
                          "escalationRole":{"type":"string"},
                          "isInterrupting":{"type":"boolean"}
                        },
                        "additionalProperties":false
                      }
                    },
                    "additionalProperties":false
                  }
                }
              },
              "additionalProperties":false
            },
            "roles":{
              "type":"array",
              "items":{
                "type":"object",
                "required":["name"],
                "properties":{
                  "name":{"type":"string","minLength":1},
                  "description":{"type":"string"},
                  "grantedCapabilities":{"type":"array","items":{"type":"string"}}
                },
                "additionalProperties":false
              }
            },
            "capabilities":{
              "type":"array",
              "items":{
                "type":"object",
                "required":["code"],
                "properties":{
                  "code":{"type":"string","minLength":1},
                  "description":{"type":"string"}
                },
                "additionalProperties":false
              }
            }
          },
          "additionalProperties":false
        }
        """);

    public static JObject StartWorkflow() => JObject.Parse(
        """
        {
          "type":"object",
          "properties":{
            "workflowClassId":{"type":"string","format":"uuid"},
            "workflowDefinitionId":{"type":"string","format":"uuid"},
            "workflowName":{"type":"string"},
            "version":{"type":"integer"},
            "initialStepId":{"type":"string"},
            "correlationId":{"type":"string","format":"uuid"},
            "tenantId":{"type":"string","format":"uuid"}
          },
          "additionalProperties":false
        }
        """);

    public static JObject PublishEvent() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["workflowInstanceId","eventType"],
          "properties":{
            "workflowInstanceId":{"type":"string","format":"uuid"},
            "eventType":{"type":"string","minLength":1},
            "correlationId":{"type":"string","format":"uuid"},
            "payload":{"type":"object"},
            "tenantId":{"type":"string","format":"uuid"}
          },
          "additionalProperties":false
        }
        """);

    public static JObject CompleteTask() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["workflowInstanceId","taskId"],
          "properties":{
            "workflowInstanceId":{"type":"string","format":"uuid"},
            "taskId":{"type":"string","format":"uuid"},
            "correlationId":{"type":"string","format":"uuid"},
            "tenantId":{"type":"string","format":"uuid"}
          },
          "additionalProperties":false
        }
        """);

    public static JObject ListWorkflowInstances() => JObject.Parse(
        """
        {
          "type":"object",
          "properties":{
            "status":{"type":"string"},
            "tenantId":{"type":"string","format":"uuid"}
          },
          "additionalProperties":false
        }
        """);

    public static JObject GetWorkflowHistory() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["workflowInstanceId"],
          "properties":{
            "workflowInstanceId":{"type":"string","format":"uuid"},
            "tenantId":{"type":"string","format":"uuid"}
          },
          "additionalProperties":false
        }
        """);

    public static JObject SimulateWorkflowClass() => JObject.Parse(
        """
        {
          "type":"object",
          "properties":{
            "id":{
              "type":"string",
              "format":"uuid",
              "description":"Optional ID of an existing draft or published WorkflowClass to simulate."
            },
            "blueprint":{
              "type":"object",
              "description":"Optional inline WorkflowClass blueprint object to simulate without requiring an existing draft."
            },
            "payload":{
              "type":"object",
              "description":"Business context payload dictionary used to evaluate decision conditions and state machine transition guards."
            },
            "role":{
              "type":"string",
              "description":"Simulated user role invoking tasks (e.g. 'User', 'Manager', 'Director', 'Admin'). Defaults to 'User'."
            },
            "events":{
              "type":"array",
              "items":{"type":"string"},
              "description":"Optional sequence of event IDs to simulate dispatching sequentially (e.g. ['EVT-SUBMIT', 'EVT-APPROVE']). Automated steps advance automatically."
            },
            "maxSteps":{
              "type":"integer",
              "minimum":1,
              "maximum":100,
              "default":25,
              "description":"Maximum step evaluation cap to prevent infinite loops."
            },
            "simulateFailureAtStep":{
              "type":"string",
              "description":"Optional step ID where a step failure should be simulated to verify OnFailure compensating actions and Saga rollback."
            },
            "tenantId":{
              "type":"string",
              "format":"uuid",
              "description":"Tenant ID scope (optional for inline blueprints, required when querying by id in stdio)."
            }
          },
          "additionalProperties":false
        }
        """);

    public static JObject AttachStepAction() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["stepId","hook","action"],
          "properties":{
            "id":{"type":"string","format":"uuid","description":"WorkflowClass draft ID (required if blueprint not supplied)."},
            "blueprint":{"type":"object","description":"Inline WorkflowClass blueprint (alternative to id)."},
            "stepId":{"type":"string","minLength":1,"description":"Target step ID to attach action hook to."},
            "hook":{"type":"string","enum":["OnEntry","OnExit","OnFailure"],"description":"Lifecycle phase to trigger action (OnEntry, OnExit, or OnFailure for Saga rollback)."},
            "actionIndex":{"type":"integer","minimum":0,"description":"Optional 0-based index to overwrite an existing action."},
            "action":{
              "type":"object",
              "required":["actionType"],
              "properties":{
                "actionType":{"type":"string","enum":["Webhook","Notification","PublishEvent"]},
                "target":{"type":"string","description":"Target URL, recipient role/user, or domain event name."},
                "url":{"type":"string","description":"Webhook destination URL (supports dynamic tokens like {{OrderId}})."},
                "method":{"type":"string","enum":["POST","GET","PUT"],"default":"POST"},
                "template":{"type":"string","description":"Message template string with optional {{Expression}} placeholders."},
                "payloadMapping":{"type":"object","description":"Key-to-expression mapping for dynamic payload transformation."},
                "condition":{"type":"string","description":"Dynamic boolean expression guard required for execution."},
                "headers":{"type":"object","description":"Custom HTTP headers."},
                "signPayload":{"type":"boolean","default":true,"description":"Attach HMAC-SHA256 signature."},
                "secretName":{"type":"string","description":"Optional custom secret name."}
              },
              "additionalProperties":false
            },
            "tenantId":{"type":"string","format":"uuid"}
          },
          "additionalProperties":false
        }
        """);

    public static JObject RemoveStepAction() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["stepId","hook"],
          "properties":{
            "id":{"type":"string","format":"uuid"},
            "blueprint":{"type":"object"},
            "stepId":{"type":"string","minLength":1},
            "hook":{"type":"string","enum":["OnEntry","OnExit","OnFailure"]},
            "actionIndex":{"type":"integer","minimum":0,"description":"0-based index of the action to remove."},
            "actionType":{"type":"string","enum":["Webhook","Notification","PublishEvent"]},
            "target":{"type":"string"},
            "tenantId":{"type":"string","format":"uuid"}
          },
          "additionalProperties":false
        }
        """);

    public static JObject ListStepActions() => JObject.Parse(
        """
        {
          "type":"object",
          "properties":{
            "id":{"type":"string","format":"uuid"},
            "blueprint":{"type":"object"},
            "stepId":{"type":"string","description":"Optional step filter."},
            "hook":{"type":"string","enum":["OnEntry","OnExit","OnFailure"],"description":"Optional hook filter."},
            "tenantId":{"type":"string","format":"uuid"}
          },
          "additionalProperties":false
        }
        """);

    public static JObject ListDeadLetters() => JObject.Parse(
        """
        {
          "type":"object",
          "properties":{
            "tenantId":{"type":"string","format":"uuid","description":"Optional tenant UUID filter."},
            "type":{"type":"string","description":"Optional event or action type filter (e.g. WorkflowAction:Webhook)."},
            "limit":{"type":"integer","minimum":1,"maximum":200,"default":50,"description":"Maximum number of dead letters to retrieve."}
          },
          "additionalProperties":false
        }
        """);

    public static JObject RetryDeadLetter() => JObject.Parse(
        """
        {
          "type":"object",
          "properties":{
            "id":{"type":"string","format":"uuid","description":"UUID of the dead letter outbox message to replay."},
            "all":{"type":"boolean","default":false,"description":"Set to true to replay all dead letters for the tenant."},
            "type":{"type":"string","description":"Optional filter when replaying all dead letters."},
            "tenantId":{"type":"string","format":"uuid","description":"Optional tenant UUID."}
          },
          "additionalProperties":false
        }
        """);

    public static JObject PurgeDeadLetter() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["id"],
          "properties":{
            "id":{"type":"string","format":"uuid","description":"UUID of the dead letter outbox message to permanently purge."},
            "tenantId":{"type":"string","format":"uuid","description":"Optional tenant UUID."}
          },
          "additionalProperties":false
        }
        """);

    public static JObject VerifyWebhookSignature() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["payload"],
          "properties":{
            "payload":{"type":"string","description":"The raw JSON payload to sign or verify."},
            "secret":{"type":"string","description":"Optional signing secret (uses tenant secret by default)."},
            "signature":{"type":"string","description":"Optional signature header (t=...,v1=...) to verify against payload."},
            "timestamp":{"type":"integer","description":"Optional timestamp to use for signature."},
            "tenantId":{"type":"string","format":"uuid","description":"Optional tenant UUID."}
          },
          "additionalProperties":false
        }
        """);

    public static JObject TestWebhookEndpoint() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["url"],
          "properties":{
            "url":{"type":"string","description":"Target endpoint URL to test with a signed ping."},
            "method":{"type":"string","enum":["POST","GET","PUT"],"default":"POST","description":"HTTP method."},
            "payload":{"type":"object","description":"Optional custom JSON payload."},
            "headers":{"type":"object","description":"Optional custom HTTP request headers."},
            "signPayload":{"type":"boolean","default":true,"description":"Whether to attach HMAC-SHA256 signature header."},
            "tenantId":{"type":"string","format":"uuid","description":"Optional tenant UUID."}
          },
          "additionalProperties":false
        }
        """);

    public static JObject RotateWebhookSecret() => JObject.Parse(
        """
        {
          "type":"object",
          "properties":{
            "tenantId":{"type":"string","format":"uuid","description":"Tenant UUID whose signing secret should be rotated."}
          },
          "additionalProperties":false
        }
        """);

    public static JObject GetInstanceActionHistory() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["workflowInstanceId"],
          "properties":{
            "workflowInstanceId":{"type":"string","format":"uuid","description":"Workflow instance UUID to retrieve action execution history for."},
            "stepId":{"type":"string","description":"Optional step ID filter (e.g. 'SubmitStep')."},
            "actionType":{"type":"string","enum":["Webhook","Notification","PublishEvent"],"description":"Optional action type filter."},
            "status":{"type":"string","enum":["Succeeded","Failed"],"description":"Optional status filter."},
            "limit":{"type":"integer","minimum":1,"maximum":200,"default":50,"description":"Maximum number of records to return (1-200)."},
            "tenantId":{"type":"string","format":"uuid","description":"Optional tenant UUID."}
          },
          "additionalProperties":false
        }
        """);
}
