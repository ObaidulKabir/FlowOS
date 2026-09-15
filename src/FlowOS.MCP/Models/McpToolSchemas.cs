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
            "contextSchema":{"type":["string","null"],"description":"Optional canonical JSON Schema used by reusable workflow context bindings."},
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
                      "condition":{"type":["string","null"],"description":"Canonical transition guard expression."},
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
                      "stepType":{"type":"string","enum":["Command","SystemTask","HumanTask","Timer","Decision","End","Fork","Join","SubWorkflow"]},
                      "decisionProvider":{"type":"string","description":"Optional decision plugin provider name for Decision steps (e.g. 'default', 'risk-v2')."},
                      "subWorkflow":{
                        "type":"object",
                        "properties":{
                          "workflowDefinitionId":{"type":"string","format":"uuid"},
                          "workflowClassId":{"type":"string","format":"uuid"},
                          "workflowName":{"type":"string"},
                          "version":{"type":"integer","minimum":1},
                          "inputMapping":{"type":"object","description":"Child input key -> expression evaluated against parent payload."},
                          "outputMapping":{"type":"object","description":"Parent output key -> expression evaluated against child completion payload."}
                        },
                        "additionalProperties":false
                      },
                      "nextSteps":{"type":"object","additionalProperties":{"type":"string"}},
                      "requiredRoles":{"type":"array","items":{"type":"string"}},
                      "conditions":{"type":"object","additionalProperties":{"type":"string"}},
                      "onEntry":{
                        "type":"array",
                        "items":{"type":"object","required":["actionType"],"properties":{"actionType":{"type":"string","description":"Built-in: Webhook/Notification/PublishEvent/InvokeCapability, or plugin alias (plugin:* / plugin.*)."},"target":{"type":"string"},"capability":{"type":"string"},"url":{"type":"string"}}}
                      },
                      "onExit":{
                        "type":"array",
                        "items":{"type":"object","required":["actionType"],"properties":{"actionType":{"type":"string","description":"Built-in: Webhook/Notification/PublishEvent/InvokeCapability, or plugin alias (plugin:* / plugin.*)."},"target":{"type":"string"},"capability":{"type":"string"},"url":{"type":"string"}}}
                      },
                      "onFailure":{
                        "type":"array",
                        "items":{"type":"object","required":["actionType"],"properties":{"actionType":{"type":"string","description":"Built-in: Webhook/Notification/PublishEvent/InvokeCapability, or plugin alias (plugin:* / plugin.*)."},"target":{"type":"string"},"capability":{"type":"string"},"url":{"type":"string"}}}
                      },
                      "sla":{
                        "type":"object",
                        "required":["duration","timeoutEvent"],
                        "properties":{
                          "duration":{"type":"string","minLength":1},
                          "timeoutEvent":{"type":"string","minLength":1},
                          "escalationStepId":{"type":"string"},
                          "escalationRole":{"type":"string"},
                          "isInterrupting":{"type":"boolean"},
                          "reminders":{
                            "type":"array",
                            "description":"Multi-tier intermediate countdown reminders fired before SLA timeout (negative offset, e.g. '-2h') or after step start (positive, e.g. '30m'). Automatically cancelled on step completion.",
                            "items":{
                              "type":"object",
                              "required":["duration","triggerEvent"],
                              "properties":{
                                "duration":{"type":"string","minLength":1,"description":"Duration offset, e.g. '-2h', '-30m', '1d'."},
                                "triggerEvent":{"type":"string","minLength":1,"description":"Event published when reminder fires. Must be defined in blueprint events."}
                              },
                              "additionalProperties":false
                            }
                          }
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
            "contextBindingId":{"type":"string","format":"uuid"},
            "contextType":{"type":"string","minLength":1},
            "version":{"type":"integer"},
            "initialStepId":{"type":"string"},
            "correlationId":{"type":"string","format":"uuid"},
            "idempotencyKey":{"type":"string","minLength":8},
            "tenantId":{"type":"string","format":"uuid"},
            "payload":{"type":"object","description":"Optional initial workflow payload containing event dates or business context for relative timers and steps."},
            "businessReference":{
              "type":"object",
              "properties":{
                "sourceSystem":{"type":"string"},
                "externalEntityId":{"type":"string"},
                "metadata":{"type":"object","additionalProperties":{"type":"string"}}
              },
              "additionalProperties":false
            }
          },
          "additionalProperties":false
        }
        """);

    public static JObject CreateContextBinding() => JObject.Parse(
        $$"""
        {
          "type":"object",
          "required":["sourceWorkflowClassId","contextType","name","definition"],
          "properties":{
            "sourceWorkflowClassId":{"type":"string","format":"uuid"},
            "contextType":{"type":"string","minLength":1},
            "name":{"type":"string","minLength":1},
            "definition":{{ContextBindingDefinitionSchema()}},
            "tenantId":{"type":"string","format":"uuid"}
          },
          "additionalProperties":false
        }
        """);

    public static JObject UpdateContextBinding() => JObject.Parse(
        $$"""
        {
          "type":"object",
          "required":["id","definition"],
          "properties":{
            "id":{"type":"string","format":"uuid"},
            "sourceWorkflowClassId":{"type":"string","format":"uuid"},
            "definition":{{ContextBindingDefinitionSchema()}},
            "tenantId":{"type":"string","format":"uuid"}
          },
          "additionalProperties":false
        }
        """);

    public static JObject ContextBindingById(bool requireConfirmation = false)
    {
        var properties = new JObject
        {
            ["id"] = new JObject
            {
                ["type"] = "string",
                ["format"] = "uuid"
            },
            ["tenantId"] = new JObject
            {
                ["type"] = "string",
                ["format"] = "uuid"
            }
        };
        if (requireConfirmation)
        {
            properties["confirmHumanApproval"] = new JObject
            {
                ["type"] = "boolean",
                ["description"] = "Explicit confirmation required for activation and archival."
            };
        }

        return new JObject
        {
            ["type"] = "object",
            ["required"] = new JArray("id"),
            ["properties"] = properties,
            ["additionalProperties"] = false
        };
    }

    public static JObject ListContextBindings() => JObject.Parse(
        """
        {
          "type":"object",
          "properties":{
            "sourceWorkflowClassId":{"type":"string","format":"uuid"},
            "tenantId":{"type":"string","format":"uuid"}
          },
          "additionalProperties":false
        }
        """);

    public static JObject SimulateContextBinding() => JObject.Parse(
        """
        {
          "type":"object",
          "oneOf":[
            {"required":["contextBindingId"]},
            {"required":["contextType"]}
          ],
          "properties":{
            "contextBindingId":{
              "type":"string",
              "format":"uuid",
              "description":"Tenant-scoped workflow context binding UUID."
            },
            "contextType":{
              "type":"string",
              "minLength":1,
              "description":"Tenant-unique business context type. Use instead of contextBindingId."
            },
            "revision":{
              "type":"string",
              "enum":["draft","active"],
              "default":"draft",
              "description":"Simulate the saved draft or the exact pinned active runtime revision."
            },
            "initialPayload":{
              "type":"object",
              "description":"Business source payload projected through the binding inputMapping."
            },
            "roles":{
              "type":"array",
              "items":{"type":"string","minLength":1},
              "description":"Default simulated business roles used by task and state-machine role checks."
            },
            "events":{
              "type":"array",
              "maxItems":100,
              "items":{
                "type":"object",
                "required":["eventType"],
                "properties":{
                  "eventType":{"type":"string","minLength":1},
                  "payload":{"type":"object"},
                  "roles":{"type":"array","items":{"type":"string","minLength":1}}
                },
                "additionalProperties":false
              },
              "description":"Ordered contextual business events with optional source payload and role overrides."
            },
            "maxSteps":{
              "type":"integer",
              "minimum":1,
              "maximum":100,
              "default":25
            },
            "tenantId":{"type":"string","format":"uuid"}
          },
          "additionalProperties":false
        }
        """);

    private static string ContextBindingDefinitionSchema() =>
        """
        {
          "type":"object",
          "required":["entityType"],
          "properties":{
            "entityType":{"type":"string","minLength":1},
            "eventAliases":{"type":"object","additionalProperties":{"type":"string"}},
            "roleOverrides":{"type":"object","additionalProperties":{"type":"string"}},
            "capabilityOverrides":{"type":"object","additionalProperties":{"type":"string"}},
            "inputMapping":{"type":"object","additionalProperties":{"type":"string"}},
            "eventInputMappings":{
              "type":"object",
              "additionalProperties":{"type":"object","additionalProperties":{"type":"string"}}
            },
            "conditionParameters":{"type":"object","additionalProperties":true},
            "decisionProviderOverrides":{"type":"object","additionalProperties":{"type":"string"}},
            "sourcePayloadSchema":{"type":["string","null"]},
            "eventSourcePayloadSchemas":{"type":"object","additionalProperties":{"type":"string"}},
            "metadata":{"type":"object","additionalProperties":{"type":"string"}}
          },
          "additionalProperties":false
        }
        """;

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
            "idempotencyKey":{"type":"string","minLength":8},
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
            "idempotencyKey":{"type":"string","minLength":8},
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
            "status":{"type":"string","description":"Optional status filter (e.g. Running, Completed, Failed)."},
            "parentWorkflowInstanceId":{"type":"string","format":"uuid","description":"Optional parent workflow instance UUID to filter child subworkflows."},
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
            "subWorkflows":{
              "type":"object",
              "description":"Optional dictionary of child WorkflowClass blueprint objects keyed by workflow name, class ID, or step ID to simulate child subworkflows inline."
            },
            "autoCompleteSubWorkflows":{
              "type":"boolean",
              "default":false,
              "description":"When true, automatically resolves subworkflow steps with completion events without requiring child blueprints or events queue."
            },
            "autoAdvanceTimers":{
              "type":"boolean",
              "default":false,
              "description":"When true, automatically elapses timer steps (advancing to their next step) without requiring events queue or waiting."
            },
            "childEvents":{
              "type":"array",
              "items":{"type":"string"},
              "description":"Optional sequence of event IDs specifically for child subworkflow execution."
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

    public static JObject SimulateSubWorkflow() => JObject.Parse(
        """
        {
          "type":"object",
          "properties":{
            "parentBlueprint":{
              "type":"object",
              "description":"Optional inline parent WorkflowClass blueprint containing the SubWorkflow step."
            },
            "blueprint":{
              "type":"object",
              "description":"Alternative alias for parentBlueprint."
            },
            "parentWorkflowClassId":{
              "type":"string",
              "format":"uuid",
              "description":"Optional ID of an existing draft or published parent WorkflowClass in storage."
            },
            "id":{
              "type":"string",
              "format":"uuid",
              "description":"Alternative alias for parentWorkflowClassId."
            },
            "stepId":{
              "type":"string",
              "description":"Optional SubWorkflow step ID to simulate. Defaults to the first SubWorkflow step in the parent blueprint."
            },
            "childBlueprint":{
              "type":"object",
              "description":"Optional inline child WorkflowClass blueprint to execute as the subworkflow."
            },
            "childWorkflowClassId":{
              "type":"string",
              "format":"uuid",
              "description":"Optional UUID of the child WorkflowClass in storage."
            },
            "childWorkflowName":{
              "type":"string",
              "description":"Optional name of the child WorkflowClass."
            },
            "subWorkflows":{
              "type":"object",
              "description":"Optional dictionary of child blueprints keyed by name or ID."
            },
            "payload":{
              "type":"object",
              "description":"Initial parent business context payload to be passed and mapped into the child workflow."
            },
            "parentPayload":{
              "type":"object",
              "description":"Alternative alias for payload."
            },
            "role":{
              "type":"string",
              "description":"Simulated user role invoking tasks. Defaults to 'User'."
            },
            "events":{
              "type":"array",
              "items":{"type":"string"},
              "description":"Optional sequence of event IDs for parent workflow simulation."
            },
            "childEvents":{
              "type":"array",
              "items":{"type":"string"},
              "description":"Optional sequence of event IDs specifically for child subworkflow simulation."
            },
            "maxSteps":{
              "type":"integer",
              "minimum":1,
              "maximum":100,
              "default":25,
              "description":"Maximum step evaluation cap."
            },
            "autoAdvanceTimers":{
              "type":"boolean",
              "default":false,
              "description":"When true, automatically elapses timer steps without requiring events queue or waiting."
            },
            "tenantId":{
              "type":"string",
              "format":"uuid",
              "description":"Tenant ID scope."
            }
          },
          "additionalProperties":false
        }
        """);

    public static JObject SimulateCompensationPath() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["failedStepId"],
          "properties":{
            "id":{"type":"string","format":"uuid","description":"Optional WorkflowClass ID to load from storage."},
            "blueprint":{"type":"object","description":"Optional inline WorkflowClass blueprint object."},
            "failedStepId":{"type":"string","minLength":1,"description":"Step where failure occurred and compensation should start."},
            "executedStepIds":{"type":"array","items":{"type":"string"},"description":"Optional actual executed step sequence. If omitted, uses declared step order up to failedStepId."},
            "tenantId":{"type":"string","format":"uuid","description":"Optional tenant UUID."}
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
                "actionType":{"type":"string","description":"Built-in: Webhook/Notification/PublishEvent/InvokeCapability, or plugin alias (plugin:* / plugin.*)."},
                "target":{"type":"string","description":"Target URL, recipient role/user, or domain event name."},
                "capability":{"type":"string","description":"Capability binding name for InvokeCapability actions (e.g. payment.refund.v1)."},
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
            "actionType":{"type":"string","description":"Optional action type selector; supports built-ins and plugin aliases."},
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

    public static JObject RegisterCapabilityBinding() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["capabilityName","endpointUrl"],
          "properties":{
            "capabilityName":{"type":"string","minLength":1,"description":"Unique capability key (e.g. payment.refund.v1)."},
            "transport":{"type":"string","enum":["http"],"default":"http"},
            "endpointUrl":{"type":"string","description":"Absolute HTTP/HTTPS endpoint of the capability worker."},
            "authRef":{"type":"string","description":"Optional auth reference name passed as x-flowos-auth-ref."},
            "requestSchemaVersion":{"type":"string","description":"Optional request contract version label."},
            "responseSchemaVersion":{"type":"string","description":"Optional response contract version label."},
            "retryPolicy":{"type":"string","default":"default","description":"Logical retry policy profile name."},
            "timeoutMs":{"type":"integer","minimum":1000,"maximum":120000,"default":10000},
            "isEnabled":{"type":"boolean","default":true},
            "tenantId":{"type":"string","format":"uuid","description":"Optional tenant UUID."}
          },
          "additionalProperties":false
        }
        """);

    public static JObject ListCapabilityBindings() => JObject.Parse(
        """
        {
          "type":"object",
          "properties":{
            "capabilityName":{"type":"string","description":"Optional capability key filter."},
            "enabledOnly":{"type":"boolean","description":"Optional filter to list only enabled bindings."},
            "tenantId":{"type":"string","format":"uuid","description":"Optional tenant UUID."}
          },
          "additionalProperties":false
        }
        """);

    public static JObject ValidateCapabilityBinding() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["capabilityName"],
          "properties":{
            "capabilityName":{"type":"string","minLength":1,"description":"Capability key to validate."},
            "tenantId":{"type":"string","format":"uuid","description":"Optional tenant UUID."}
          },
          "additionalProperties":false
        }
        """);

    public static JObject RegisterPluginBinding() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["bindingType","sourceName","providerName"],
          "properties":{
            "bindingType":{"type":"string","enum":["action","decision"],"description":"Binding category."},
            "sourceName":{"type":"string","minLength":1,"description":"Blueprint-side actionType or decisionProvider name."},
            "providerName":{"type":"string","minLength":1,"description":"Concrete server-side plugin provider name to invoke."},
            "isEnabled":{"type":"boolean","default":true},
            "tenantId":{"type":"string","format":"uuid","description":"Optional tenant UUID."}
          },
          "additionalProperties":false
        }
        """);

    public static JObject ListPluginBindings() => JObject.Parse(
        """
        {
          "type":"object",
          "properties":{
            "bindingType":{"type":"string","enum":["action","decision"],"description":"Optional binding category filter."},
            "sourceName":{"type":"string","description":"Optional source key filter."},
            "enabledOnly":{"type":"boolean","description":"Optional filter to list only enabled bindings."},
            "tenantId":{"type":"string","format":"uuid","description":"Optional tenant UUID."}
          },
          "additionalProperties":false
        }
        """);

    public static JObject ResolvePluginBinding() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["bindingType","sourceName"],
          "properties":{
            "bindingType":{"type":"string","enum":["action","decision"],"description":"Binding category."},
            "sourceName":{"type":"string","minLength":1,"description":"Blueprint-side actionType or decisionProvider name."},
            "tenantId":{"type":"string","format":"uuid","description":"Optional tenant UUID."}
          },
          "additionalProperties":false
        }
        """);

    public static JObject ListRegisteredPlugins() => JObject.Parse(
        """
        {
          "type":"object",
          "properties":{
            "includeWildcard":{"type":"boolean","default":true,"description":"Include wildcard action plugin '*' in the response when registered."}
          },
          "additionalProperties":false
        }
        """);

    public static JObject TestActionPlugin() => JObject.Parse(
        """
        {
          "type":"object",
          "properties":{
            "actionType":{"type":"string","minLength":1,"description":"Name of the action plugin to test (e.g. Email, Slack, WhatsApp, Webhook, Notification, PublishEvent, InvokeCapability)."},
            "target":{"type":"string","description":"Optional target (e.g. email address, phone number, channel name #alerts, or webhook URL)."},
            "template":{"type":"string","description":"Optional template string, message subject, or text template."},
            "url":{"type":"string","description":"Optional webhook or media endpoint URL."},
            "payload":{"type":"object","description":"Optional action payload dictionary containing channel-specific properties."},
            "tenantId":{"type":"string","format":"uuid","description":"Optional tenant UUID context."}
          },
          "required":["actionType"],
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
            "actionType":{"type":"string","description":"Optional action type filter (built-ins or plugin aliases)."},
            "status":{"type":"string","enum":["Succeeded","Failed"],"description":"Optional status filter."},
            "limit":{"type":"integer","minimum":1,"maximum":200,"default":50,"description":"Maximum number of records to return (1-200)."},
            "tenantId":{"type":"string","format":"uuid","description":"Optional tenant UUID."}
          },
          "additionalProperties":false
        }
        """);

    public static JObject RegisterIdempotencyKey() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["operationName","idempotencyKey"],
          "properties":{
            "operationName":{"type":"string","minLength":1,"description":"Operation name to reserve idempotency for (e.g. start_workflow)."},
            "idempotencyKey":{"type":"string","minLength":8,"description":"Client-generated idempotency key."},
            "tenantId":{"type":"string","format":"uuid","description":"Optional tenant UUID."}
          },
          "additionalProperties":false
        }
        """);

    public static JObject InspectIdempotencyStatus() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["operationName","idempotencyKey"],
          "properties":{
            "operationName":{"type":"string","minLength":1},
            "idempotencyKey":{"type":"string","minLength":8},
            "tenantId":{"type":"string","format":"uuid","description":"Optional tenant UUID."}
          },
          "additionalProperties":false
        }
        """);

    public static JObject PreviewRetryPolicy() => JObject.Parse(
        """
        {
          "type":"object",
          "properties":{
            "currentRetryCount":{"type":"integer","minimum":0,"default":0},
            "maxRetries":{"type":"integer","minimum":1,"default":5},
            "baseDelaySeconds":{"type":"integer","minimum":1,"default":2},
            "strategy":{"type":"string","enum":["exponential","linear","constant"],"default":"exponential"},
            "maxDelaySeconds":{"type":"integer","minimum":1,"default":3600},
            "errorMessage":{"type":"string","description":"Optional error text to classify as transient or permanent."}
          },
          "additionalProperties":false
        }
        """);

    public static JObject ReplayWorkflowHistory() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["workflowInstanceId"],
          "properties":{
            "workflowInstanceId":{"type":"string","format":"uuid","description":"Workflow instance UUID whose immutable event stream should be reconstructed into step snapshots."},
            "tenantId":{"type":"string","format":"uuid","description":"Optional tenant UUID."}
          },
          "additionalProperties":false
        }
        """);

    public static JObject ForkWorkflowSimulation() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["workflowInstanceId","alternativeEvent"],
          "properties":{
            "workflowInstanceId":{"type":"string","format":"uuid","description":"Workflow instance UUID to fork from."},
            "targetStepIndex":{"type":"integer","minimum":0,"default":0,"description":"Historical snapshot index to clone as the what-if origin."},
            "alternativeEvent":{"type":"string","minLength":1,"description":"Alternate event type to evaluate (e.g. EVT-REJECT)."},
            "alternativePayload":{"type":"object","description":"Optional alternate payload used only inside the sandbox."},
            "simulatedRoles":{"type":"array","items":{"type":"string","minLength":1},"description":"Business roles used by contextual role and state-machine guard checks."},
            "tenantId":{"type":"string","format":"uuid","description":"Optional tenant UUID."}
          },
          "additionalProperties":false
        }
        """);

    public static JObject PlanWorkflowCompensationPath() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["workflowInstanceId"],
          "properties":{
            "workflowInstanceId":{"type":"string","format":"uuid","description":"Workflow instance UUID to evaluate compensation from runtime history."},
            "failedStepId":{"type":"string","description":"Optional failed step ID. If omitted, current/latest replay step is used."},
            "tenantId":{"type":"string","format":"uuid","description":"Optional tenant UUID."}
          },
          "additionalProperties":false
        }
        """);

    public static JObject GenerateBlueprintFromNaturalLanguage() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["prompt"],
          "properties":{
            "prompt":{"type":"string","minLength":1,"description":"Natural language description of the desired workflow (e.g. 'Insurance Claim with parallel damage inspection and medical assessment')."},
            "currentBlueprint":{"type":"object","description":"Optional existing WorkflowClassBlueprint to refine or augment."},
            "mode":{"type":"string","enum":["create","refine","template"],"default":"create","description":"Create a business-specific workflow, refine an existing one, or generate a reusable binding-ready template."},
            "tenantId":{"type":"string","format":"uuid","description":"Optional tenant UUID."}
          },
          "additionalProperties":false
        }
        """);

    public static JObject RefineBlueprintFromNaturalLanguage() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["prompt","currentBlueprint"],
          "properties":{
            "prompt":{"type":"string","minLength":1,"description":"Natural language refinement prompt (e.g. 'Add 24h SLA timeout and manager escalation')."},
            "currentBlueprint":{"type":"object","description":"Existing WorkflowClassBlueprint to refine."},
            "tenantId":{"type":"string","format":"uuid","description":"Optional tenant UUID."}
          },
          "additionalProperties":false
        }
        """);

    public static JObject GetSubWorkflowTree() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["workflowInstanceId"],
          "properties":{
            "workflowInstanceId":{"type":"string","format":"uuid","description":"Workflow instance UUID to retrieve subworkflow execution tree for."},
            "tenantId":{"type":"string","format":"uuid","description":"Optional tenant UUID."}
          },
          "additionalProperties":false
        }
        """);

    public static JObject SimulateParallelExecution() => JObject.Parse(
        """
        {
          "type":"object",
          "properties":{
            "id":{"type":"string","format":"uuid","description":"Optional stored workflow class UUID."},
            "blueprint":{"type":"object","description":"Optional inline WorkflowClassBlueprint with Fork/Join steps."},
            "payload":{"type":"object","description":"Optional simulation context variables."},
            "tenantId":{"type":"string","format":"uuid","description":"Optional tenant UUID."}
          },
          "additionalProperties":false
        }
        """);
}

