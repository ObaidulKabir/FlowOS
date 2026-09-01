using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Models;

public static class McpToolSchemas
{
    public static JObject NoArguments() => JObject.Parse(
        """{"type":"object","properties":{},"additionalProperties":false}""");

    public static JObject TenantOptional() => JObject.Parse(
        """{"type":"object","properties":{"tenantId":{"type":"string","format":"uuid"}},"additionalProperties":false}""");

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
                      "conditions":{"type":"object","additionalProperties":{"type":"string"}}
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
}
