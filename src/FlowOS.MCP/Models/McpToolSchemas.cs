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
          "required":["workflowInstanceId"],
          "properties":{
            "workflowInstanceId":{"type":"string","format":"uuid"},
            "agentId":{"type":"string","description":"Attribution / fixture id. Omit so the factory follows step.agentProvider (TenantLlmWorkflowAgent). Use RiskAnalysisAgent only for flowos-risk / no provider."},
            "tenantId":{"type":"string","format":"uuid"},
            "objective":{"type":"string","maxLength":500}
          },
          "additionalProperties":false
        }
        """);

    public static JObject RunAgentTask() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["workflowInstanceId"],
          "properties":{
            "workflowInstanceId":{"type":"string","format":"uuid"},
            "agentId":{"type":"string","description":"Optional attribution id. Omit so the hosted loop uses step.agentProvider (openai/anthropic/azure-openai/google/custom → TenantLlmWorkflowAgent). RiskAnalysisAgent is the no-key fixture."},
            "tenantId":{"type":"string","format":"uuid"}
          },
          "additionalProperties":false
        }
        """);

    public static JObject GetAgentExecutionHistory() => JObject.Parse(
        """
        {
          "type":"object",
          "properties":{
            "workflowInstanceId":{"type":"string","format":"uuid","description":"Optional workflow instance filter. Omit for tenant-wide history."},
            "fromUtc":{"type":"string","format":"date-time","description":"Inclusive UTC start. Defaults to 30 days before toUtc."},
            "toUtc":{"type":"string","format":"date-time","description":"Exclusive UTC end. Defaults to the current UTC time."},
            "status":{"type":"string","enum":["Running","Succeeded","Failed","Cancelled"]},
            "limit":{"type":"integer","minimum":1,"maximum":200,"default":100},
            "tenantId":{"type":"string","format":"uuid"}
          },
          "additionalProperties":false
        }
        """);

    public static JObject GetAgentEvaluationMetrics() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["fromUtc","toUtc"],
          "properties":{
            "fromUtc":{"type":"string","format":"date-time","description":"Inclusive UTC execution-start boundary."},
            "toUtc":{"type":"string","format":"date-time","description":"Exclusive UTC execution-start boundary. Window maximum is 90 days."},
            "tenantId":{"type":"string","format":"uuid"}
          },
          "additionalProperties":false
        }
        """);

    public static JObject GetAgentContext() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["workflowInstanceId"],
          "properties":{
            "workflowInstanceId":{"type":"string","format":"uuid","description":"Live workflow instance whose current step is composed into Agent Context."},
            "objective":{"type":"string","maxLength":500},
            "prefetch":{"type":"boolean","default":true,"description":"When true, FlowOS runs declared read resource plugins into Data.ToolResults. Default true."},
            "tenantId":{"type":"string","format":"uuid"}
          },
          "additionalProperties":false
        }
        """);

    public static JObject PreviewAgentContext() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["workflowClassId","stepId"],
          "properties":{
            "workflowClassId":{"type":"string","format":"uuid","description":"Draft or published workflow class to preview. Does not start an instance."},
            "stepId":{"type":"string","minLength":1,"description":"Waiting step whose agentPrompt/agentProvider/agentTools are composed."},
            "contextBindingId":{"type":"string","format":"uuid","description":"Optional binding that supplies policyGuideline."},
            "canonicalContext":{"type":"object","additionalProperties":true,"description":"Optional sample canonical case fields. The model never sees tenant URLs."},
            "currentState":{"type":"string","description":"Optional state-machine state for legal SM events."},
            "objective":{"type":"string","maxLength":500},
            "prefetch":{"type":"boolean","default":false,"description":"Design-time default is false so preview does not call tenant APIs unless asked."},
            "tenantId":{"type":"string","format":"uuid"}
          },
          "additionalProperties":false
        }
        """);

    public static JObject UpsertAgentPrompt() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["alias"],
          "properties":{
            "alias":{"type":"string","minLength":1,"description":"Prompt name. Point the waiting step at it with agentPrompt."},
            "sourceName":{"type":"string","description":"Alias synonym for alias."},
            "kind":{"type":"string","enum":["markdown","flowos-prompt"],"default":"markdown"},
            "title":{"type":"string"},
            "system":{"type":"string"},
            "instructions":{"type":"string","description":"Prompt body the agent sees. Required on create."},
            "text":{"type":"string","description":"Alias for instructions."},
            "configuration":{
              "type":"object",
              "properties":{
                "title":{"type":"string"},
                "system":{"type":"string"},
                "instructions":{"type":"string"},
                "text":{"type":"string"}
              },
              "additionalProperties":false
            },
            "isEnabled":{"type":"boolean","default":true},
            "tenantId":{"type":"string","format":"uuid"}
          },
          "additionalProperties":false
        }
        """);

    public static JObject ListAgentPrompts() => JObject.Parse(
        """
        {
          "type":"object",
          "properties":{
            "alias":{"type":"string"},
            "enabledOnly":{"type":"boolean"},
            "tenantId":{"type":"string","format":"uuid"}
          },
          "additionalProperties":false
        }
        """);

    public static JObject GetAgentPrompt() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["alias"],
          "properties":{
            "alias":{"type":"string","minLength":1},
            "sourceName":{"type":"string"},
            "tenantId":{"type":"string","format":"uuid"}
          },
          "additionalProperties":false
        }
        """);

    public static JObject UpsertAgentProvider() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["alias"],
          "properties":{
            "alias":{"type":"string","minLength":1,"description":"Provider name. Point the waiting step at it with agentProvider."},
            "sourceName":{"type":"string","description":"Alias synonym for alias."},
            "providerName":{"type":"string","enum":["openai","anthropic","azure-openai","google","custom","flowos-risk","flowos-hosted"],"description":"Required on create. flowos-hosted is the paid FlowOS OpenAI default (no tenant key). BYO kinds store a tenant key. flowos-risk needs no key."},
            "model":{"type":"string"},
            "endpoint":{"type":"string"},
            "apiKey":{"type":"string","description":"Write-only tenant LLM key. Omit on update to keep the stored key. Never returned."},
            "configuration":{
              "type":"object",
              "properties":{
                "model":{"type":"string"},
                "endpoint":{"type":"string"},
                "apiKey":{"type":"string"}
              },
              "additionalProperties":false
            },
            "isEnabled":{"type":"boolean","default":true},
            "tenantId":{"type":"string","format":"uuid"}
          },
          "additionalProperties":false
        }
        """);

    public static JObject ListAgentProviders() => JObject.Parse(
        """
        {
          "type":"object",
          "properties":{
            "alias":{"type":"string"},
            "enabledOnly":{"type":"boolean"},
            "tenantId":{"type":"string","format":"uuid"}
          },
          "additionalProperties":false
        }
        """);

    public static JObject GetAgentProvider() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["alias"],
          "properties":{
            "alias":{"type":"string","minLength":1},
            "sourceName":{"type":"string"},
            "tenantId":{"type":"string","format":"uuid"}
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
                  "payloadSchema":{"type":["string","null"]},
                  "allowedRoles":{"type":"array","items":{"type":"string"},"description":"Inbox hint for who receives this human event. Empty does not hide step requiredRoles. Not the execution gate."},
                  "requiredCapabilities":{"type":"array","items":{"type":"string"},"description":"Execution gate, typically event.publish.<eventId>. Human events must declare at least one (GOV-002). Roles are inbox only."}
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
                      "pathLimits":{
                        "type":"object",
                        "description":"Per-event travel caps for repeatable paths (retry-password, resubmit). Key matches a nextSteps or Decision conditions key. Cyclic edges without a declaration default to maxTravels=5.",
                        "additionalProperties":{
                          "type":"object",
                          "properties":{
                            "maxTravels":{"type":"integer","minimum":1,"maximum":100,"default":5,"description":"Maximum times this edge may be traveled, including the first pass."},
                            "onExceeded":{"type":"string","description":"Step id or END taken when the cap is exceeded. If omitted, the engine fails closed."}
                          },
                          "additionalProperties":false
                        }
                      },
                      "requiredRoles":{"type":"array","items":{"type":"string"},"description":"HumanTask inbox assignment only. Who sees the task. Does not authorize execution."},
                      "allowedRoles":{"type":"array","items":{"type":"string"},"description":"Optional inbox merge with requiredRoles. Empty must not hide requiredRoles."},
                      "requiredCapabilities":{"type":"array","items":{"type":"string"},"description":"Execution gate for completing this HumanTask or its human exits (event.publish.<eventId>). requiredRoles is inbox only."},
                      "actor":{"type":"string","enum":["Human","Agent","Either"],"default":"Human","description":"Who may act on a waiting step. Human is the default. Agent/Either host a DecisionPacket and may auto-commit only when autoCommit matches."},
                      "decisionGuideline":{"type":"string","description":"Template markdown: how to decide among legal nextSteps (allowed outcomes, examples, escalate-if)."},
                      "autoCommit":{
                        "type":"object",
                        "properties":{
                          "minConfidence":{"type":"number","minimum":0,"maximum":1,"default":0.9},
                          "allowedEvents":{"type":"array","items":{"type":"string"},"description":"Subset of nextSteps keys that FlowOS may publish when confidence is in bounds. Never include TimeoutEvent."}
                        },
                        "additionalProperties":false
                      },
                      "agentProvider":{"type":"string","description":"Alias resolved against a tenant plugin binding of type agent. The binding holds provider/model/endpoint/apiKey. The key never appears in Agent Context."},
                      "agentPrompt":{"type":"string","description":"Alias of a tenant prompt binding (bindingType prompt). Create/edit the prompt independently; FlowOS loads title/system/instructions into Agent Context.Prompt."},
                      "agentTools":{"type":"array","items":{"type":"string"},"description":"Declared tools: resource plugins (LookupRecord:<connector>, QueryRecords:, FetchDocument:, SearchKnowledge:, CheckPolicy:), notify plugins (Webhook/Email/Slack/WhatsApp), and write connectors (connector:payment.refund.v1; legacy capability: prefix still accepted). Reads are prefetched; writes are not. Legal nextSteps events are always included."},
                      "conditions":{"type":"object","additionalProperties":{"type":"string"}},
                      "branches":{"type":"array","items":{"type":"string"}},
                      "joinPolicy":{"type":"string"},
                      "inboundSteps":{"type":"array","items":{"type":"string"}},
                      "onEntry":{
                        "type":"array",
                        "items":{"type":"object","required":["actionType"],"properties":{"actionType":{"type":"string","description":"Built-in: Webhook/Notification/PublishEvent/InvokeConnector (legacy InvokeCapability), or plugin alias (plugin:* / plugin.*)."},"target":{"type":"string"},"connector":{"type":"string"},"capability":{"type":"string","description":"Deprecated alias for connector."},"url":{"type":"string"}}}
                      },
                      "onExit":{
                        "type":"array",
                        "items":{"type":"object","required":["actionType"],"properties":{"actionType":{"type":"string","description":"Built-in: Webhook/Notification/PublishEvent/InvokeConnector (legacy InvokeCapability), or plugin alias (plugin:* / plugin.*)."},"target":{"type":"string"},"connector":{"type":"string"},"capability":{"type":"string","description":"Deprecated alias for connector."},"url":{"type":"string"}}}
                      },
                      "onFailure":{
                        "type":"array",
                        "items":{"type":"object","required":["actionType"],"properties":{"actionType":{"type":"string","description":"Built-in: Webhook/Notification/PublishEvent/InvokeConnector (legacy InvokeCapability), or plugin alias (plugin:* / plugin.*)."},"target":{"type":"string"},"connector":{"type":"string"},"capability":{"type":"string","description":"Deprecated alias for connector."},"url":{"type":"string"}}}
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
              "description":"Business-context grant bags compiled onto WorkflowDefinition.BusinessRoles. Never written to tenant IAM Role tables. name is the inbox label; grantedCapabilities is the execution grant.",
              "items":{
                "type":"object",
                "required":["name"],
                "properties":{
                  "name":{"type":"string","minLength":1,"description":"Inbox / grant-bag name. Not a FlowOS tenant IAM role."},
                  "description":{"type":"string"},
                  "grantedCapabilities":{"type":"array","items":{"type":"string"},"description":"Capabilities this business role may execute (e.g. event.publish.EVT-APPROVE). Director may hold Manager event grants without being the inbox role."},
                  "resolutionType":{"type":"string","enum":["Assignment","Expression","Static"]},
                  "memberExpression":{"type":"string"},
                  "staticMembers":{"type":"array","items":{"type":"string"}}
                },
                "additionalProperties":false
              }
            },
            "capabilities":{
              "type":"array",
              "description":"Declared capability catalog. Human events/steps reference these codes as the execution gate.",
              "items":{
                "type":"object",
                "required":["code"],
                "properties":{
                  "code":{"type":"string","minLength":1,"description":"Capability code, typically event.publish.<eventId>."},
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
              "description":"Simulated business-context roles (inbox labels plus grant bags). Execution is gated by grantedCapabilities / event.publish.*, not by matching the HumanTask inbox."
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
              "description":"Ordered contextual business events with optional source payload and role overrides. SLA reminder triggerEvents may be omitted; the simulator injects them in duration order before a completing nextSteps event."
            },
            "maxSteps":{
              "type":"integer",
              "minimum":1,
              "maximum":100,
              "default":25
            },
            "autoAdvanceTimers":{
              "type":"boolean",
              "default":false,
              "description":"When true, also fires HumanTask/Command SLA reminders then TimeoutEvent if no completing event remains. Use this instead of starting a live instance to prove QUOTE_RESPONSE_OVERDUE / REPAIR_OVERDUE."
            },
            "autoAdvanceAgents":{
              "type":"boolean",
              "default":true,
              "description":"When a waiting HumanTask/Command has actor Agent/Either and no completing event is queued, run the shared deterministic agent policy without a live LLM. Default suggestion is the first autoCommit.allowedEvents at confidence 1.0. Set false to wait and inspect pendingAgentTask."
            },
            "simulatedAgent":{
              "type":"object",
              "properties":{
                "event":{"type":"string","description":"Contextual legal nextSteps event to evaluate."},
                "confidence":{"type":"number","minimum":0,"maximum":1,"default":1,"description":"Compared with autoCommit.minConfidence."},
                "agentId":{"type":"string","default":"RiskAnalysisAgent","description":"Agent identity used for trace attribution."}
              },
              "additionalProperties":false,
              "description":"Optional deterministic suggestion. Explicit queued completing events still win, agent commits bypass human capability checks, and timer events remain timer-owned."
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
            "metadata":{"type":"object","additionalProperties":{"type":"string"}},
            "policyGuideline":{"type":["string","null"],"description":"Tenant overlay for how to decide in this binding without copying the template guideline."}
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
              "description":"Simulated business-context role (inbox label plus grant bag). HumanTask execution is gated by grantedCapabilities, not by matching requiredRoles. Defaults to 'User'."
            },
            "events":{
              "type":"array",
              "items":{"type":"string"},
              "description":"Optional sequence of event IDs dispatched in order. HumanTask/Timer events must match that step's NextSteps keys. SLA reminder triggerEvents may be omitted: the simulator injects them in duration order before a completing nextSteps event. TimeoutEvent is injected only when autoAdvanceTimers is true and no completing event remains. Other events are applied to the state machine when Decision or Default Command steps complete if a transition exists from the current state. Do not strip unused system events; they keep finalState in sync with step progression."
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
              "description":"When true, automatically elapses Timer steps AND HumanTask/Command SLA clocks (reminders in duration order, then TimeoutEvent) without waiting for a live clock. When false, a completing nextSteps event still fires SLA reminders that would elapse while waiting, but does not fire the timeout. Omit completing events and set this true to simulate QUOTE_RESPONSE_OVERDUE / REPAIR_OVERDUE. Do not start a live instance just to prove reminders."
            },
            "autoAdvanceAgents":{
              "type":"boolean",
              "default":true,
              "description":"When a waiting step has actor Agent/Either and no completing business event is queued, host AutoCommitEvaluator without a live LLM. Default suggestion is the first autoCommit.allowedEvents at confidence 1.0. Set false to park and inspect pendingAgentTask. TimeoutEvent stays timer-owned. Explicit events still win."
            },
            "simulatedAgent":{
              "type":"object",
              "properties":{
                "event":{"type":"string","description":"Legal nextSteps event the simulated agent suggests. Defaults to the first autoCommit.allowedEvents key."},
                "confidence":{"type":"number","minimum":0,"maximum":1,"default":1,"description":"Suggestion confidence compared to autoCommit.minConfidence."},
                "agentId":{"type":"string","default":"RiskAnalysisAgent","description":"Attributed as actor Agent:{id} on auto-commit."}
              },
              "additionalProperties":false,
              "description":"Optional simulated DecisionPacket suggestion. Does not call a tenant LLM. Used by AutoCommitEvaluator on actor Agent/Either waiting steps."
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
              "description":"Simulated business-context role (inbox label plus grant bag). Execution is gated by grantedCapabilities. Defaults to 'User'."
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
              "description":"When true, automatically elapses Timer steps and HumanTask/Command SLA reminder/timeout clocks without requiring a live waiting instance."
            },
            "autoAdvanceAgents":{
              "type":"boolean",
              "default":true,
              "description":"When a waiting child/parent step has actor Agent/Either, host AutoCommitEvaluator without a live LLM."
            },
            "simulatedAgent":{
              "type":"object",
              "properties":{
                "event":{"type":"string"},
                "confidence":{"type":"number","minimum":0,"maximum":1,"default":1},
                "agentId":{"type":"string","default":"RiskAnalysisAgent"}
              },
              "additionalProperties":false
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
                "actionType":{"type":"string","description":"Built-in: Webhook/Notification/PublishEvent/InvokeConnector (legacy InvokeCapability), or plugin alias (plugin:* / plugin.*)."},
                "target":{"type":"string","description":"Target URL, recipient role/user, or domain event name."},
                "connector":{"type":"string","description":"Connector name for InvokeConnector actions (e.g. payment.refund.v1)."},
                "capability":{"type":"string","description":"Deprecated alias for connector."},
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

    public static JObject RegisterConnector() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["endpointUrl"],
          "properties":{
            "connectorName":{"type":"string","minLength":1,"description":"Unique connector key (e.g. payment.refund.v1)."},
            "capabilityName":{"type":"string","minLength":1,"description":"Deprecated alias for connectorName."},
            "transport":{"type":"string","enum":["http"],"default":"http"},
            "endpointUrl":{"type":"string","description":"Absolute HTTP/HTTPS endpoint of the connector worker."},
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

    public static JObject ListConnectors() => JObject.Parse(
        """
        {
          "type":"object",
          "properties":{
            "connectorName":{"type":"string","description":"Optional connector key filter."},
            "capabilityName":{"type":"string","description":"Deprecated alias for connectorName."},
            "enabledOnly":{"type":"boolean","description":"Optional filter to list only enabled connectors."},
            "tenantId":{"type":"string","format":"uuid","description":"Optional tenant UUID."}
          },
          "additionalProperties":false
        }
        """);

    public static JObject ValidateConnector() => JObject.Parse(
        """
        {
          "type":"object",
          "properties":{
            "connectorName":{"type":"string","minLength":1,"description":"Connector key to validate."},
            "capabilityName":{"type":"string","minLength":1,"description":"Deprecated alias for connectorName."},
            "tenantId":{"type":"string","format":"uuid","description":"Optional tenant UUID."}
          },
          "additionalProperties":false
        }
        """);

    public static JObject RegisterCapabilityBinding() => RegisterConnector();

    public static JObject ListCapabilityBindings() => ListConnectors();

    public static JObject ValidateCapabilityBinding() => ValidateConnector();

    public static JObject RegisterPluginBinding() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["bindingType","sourceName"],
          "properties":{
            "bindingType":{"type":"string","enum":["action","decision","agent","prompt"],"description":"Binding category. Use agent for a tenant-owned model. Use prompt to create/edit named prompt text."},
            "sourceName":{"type":"string","minLength":1,"description":"Blueprint alias: actionType, decisionProvider, step.agentProvider, or step.agentPrompt."},
            "providerName":{"type":"string","minLength":1,"description":"Concrete provider. Agent: openai, anthropic, azure-openai, google, custom, flowos-risk, flowos-hosted. Prompt: markdown or flowos-prompt."},
            "isEnabled":{"type":"boolean","default":true},
            "configuration":{
              "type":"object",
              "description":"Agent: {model,endpoint,apiKey} (apiKey write-only). Prompt: {title,system,instructions} — create and edit the prompt the agent sees.",
              "properties":{
                "model":{"type":"string"},
                "endpoint":{"type":"string"},
                "apiKey":{"type":"string"},
                "title":{"type":"string"},
                "system":{"type":"string"},
                "instructions":{"type":"string"},
                "text":{"type":"string","description":"Alias for instructions."}
              },
              "additionalProperties":false
            },
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
            "bindingType":{"type":"string","enum":["action","decision","agent","prompt"],"description":"Optional binding category filter."},
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
            "bindingType":{"type":"string","enum":["action","decision","agent","prompt"],"description":"Binding category."},
            "sourceName":{"type":"string","minLength":1,"description":"Blueprint alias: actionType, decisionProvider, step.agentProvider, or step.agentPrompt."},
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
            "simulatedRoles":{"type":"array","items":{"type":"string","minLength":1},"description":"Simulated business-context roles (inbox labels plus grant bags). Execution is gated by grantedCapabilities, not by matching the HumanTask inbox."},
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

    public static JObject DiagnoseCallerPermissions() => JObject.Parse(
        """
        {
          "type":"object",
          "properties":{
            "requiredCapability":{"type":"string","minLength":1,"description":"Optional capability to evaluate, such as workflow.start or event.publish.EVT-SUBMIT."},
            "tenantId":{"type":"string","format":"uuid","description":"Optional tenant UUID; HTTP uses the authenticated tenant."}
          },
          "additionalProperties":false
        }
        """);

    public static JObject CreateTenantRole() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["roleName","confirmHumanApproval"],
          "properties":{
            "roleName":{"type":"string","minLength":1,"description":"Unique tenant IAM role name."},
            "confirmHumanApproval":{"type":"boolean","const":true,"description":"Explicit approval required for tenant IAM mutation."},
            "tenantId":{"type":"string","format":"uuid","description":"Optional tenant UUID; HTTP uses the authenticated tenant."}
          },
          "additionalProperties":false
        }
        """);

    public static JObject ChangeRoleCapability() => JObject.Parse(
        """
        {
          "type":"object",
          "required":["roleId","capabilityCode","confirmHumanApproval"],
          "properties":{
            "roleId":{"type":"string","format":"uuid","description":"Tenant IAM role UUID."},
            "capabilityCode":{"type":"string","minLength":1,"description":"Runtime capability code, such as workflow.start."},
            "confirmHumanApproval":{"type":"boolean","const":true,"description":"Explicit approval required for tenant IAM mutation."},
            "tenantId":{"type":"string","format":"uuid","description":"Optional tenant UUID; HTTP uses the authenticated tenant."}
          },
          "additionalProperties":false
        }
        """);
}

