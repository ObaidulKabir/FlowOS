using System;
using System.Collections.Generic;
using System.Linq;
using FlowOS.Domain.Validation;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowOS.Infrastructure.Services
{
    public class WorkflowJsonLinter : IWorkflowJsonLinter
    {
        public IEnumerable<LintError> Lint(string jsonContent)
        {
            var errors = new List<LintError>();
            JObject root;

            try
            {
                root = JObject.Parse(jsonContent);
            }
            catch (JsonReaderException ex)
            {
                errors.Add(new LintError("JSON-001", $"Invalid JSON: {ex.Message}", ex.LineNumber, ex.LinePosition, "", "Syntax"));
                return errors;
            }

            // 1. Validate Structure
            ValidateStructure(root, errors);

            // 2. Validate Events
            var eventIds = ValidateEvents(root, errors);

            // 3. Validate State Machine
            var states = ValidateStateMachine(root, eventIds, errors);

            // 4. Validate Workflow Steps
            ValidateWorkflow(root, eventIds, errors);

            // 5. Validate Roles & Capabilities
            ValidateRoles(root, errors);

            return errors;
        }

        private void ValidateStructure(JObject root, List<LintError> errors)
        {
            if (!root.ContainsKey("events"))
                AddError(errors, root, "STR-001", "Missing 'events' section", "root", "Structure");
            
            if (!root.ContainsKey("stateMachine"))
                AddError(errors, root, "STR-002", "Missing 'stateMachine' section", "root", "Structure");
            
            if (!root.ContainsKey("workflow"))
                AddError(errors, root, "STR-003", "Missing 'workflow' section", "root", "Structure");
        }

        private HashSet<string> ValidateEvents(JObject root, List<LintError> errors)
        {
            var eventIds = new HashSet<string>();
            var eventsToken = root["events"];

            if (eventsToken is JArray eventsArray)
            {
                foreach (var evt in eventsArray)
                {
                    var idToken = evt["eventId"];
                    if (idToken == null || idToken.Type != JTokenType.String)
                    {
                        AddError(errors, evt, "EVT-001", "Event is missing 'eventId'", "events", "Events");
                        continue;
                    }

                    var id = idToken.Value<string>();
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        AddError(errors, idToken, "EVT-002", "Event ID cannot be empty", "events", "Events");
                        continue;
                    }

                    if (!eventIds.Add(id))
                    {
                        AddError(errors, idToken, "EVT-003",($"Duplicate Event ID '{id}'"), $"events[{id}]", "Events");
                    }

                    // Check Payload Schema
                    var schemaToken = evt["payloadSchema"];
                    if (schemaToken != null && schemaToken.Type == JTokenType.String)
                    {
                        try
                        {
                            JToken.Parse(schemaToken.Value<string>());
                        }
                        catch (JsonReaderException)
                        {
                            AddError(errors, schemaToken, "EVT-004", $"Invalid JSON Schema in 'payloadSchema' for event '{id}'", $"events[{id}]", "Events");
                        }
                    }
                }
            }
            else if (eventsToken != null)
            {
                AddError(errors, eventsToken, "STR-004", "'events' must be an array", "events", "Structure");
            }

            return eventIds;
        }

        private HashSet<string> ValidateStateMachine(JObject root, HashSet<string> declaredEvents, List<LintError> errors)
        {
            var states = new HashSet<string>();
            var smToken = root["stateMachine"];
            if (smToken == null) return states;

            var statesToken = smToken["states"];
            if (statesToken is JArray statesArray)
            {
                foreach (var s in statesArray)
                {
                    if (s.Type == JTokenType.String)
                    {
                        var stateName = s.Value<string>();
                        if (!string.IsNullOrWhiteSpace(stateName))
                        {
                            if (!states.Add(stateName))
                                AddError(errors, s, "SM-001", $"Duplicate State '{stateName}'", "stateMachine.states", "StateMachine");
                        }
                    }
                }
            }

            var transitionsToken = smToken["transitions"];
            if (transitionsToken is JArray transitionsArray)
            {
                foreach (var t in transitionsArray)
                {
                    ValidateReference(t, "fromState", states, "SM-002", "Transition FromState unknown", errors);
                    ValidateReference(t, "toState", states, "SM-003", "Transition ToState unknown", errors);
                    ValidateReference(t, "eventId", declaredEvents, "SM-004", "Transition Event unknown", errors);
                }
            }

            return states;
        }

        private void ValidateWorkflow(JObject root, HashSet<string> declaredEvents, List<LintError> errors)
        {
            var wfToken = root["workflow"];
            if (wfToken == null) return;

            var stepsToken = wfToken["steps"];
            var stepIds = new HashSet<string>();
            
            if (stepsToken is JArray stepsArray)
            {
                // First pass: Collect Step IDs
                foreach (var step in stepsArray)
                {
                    var idToken = step["stepId"];
                    if (idToken != null)
                    {
                        var id = idToken.Value<string>();
                        if (!string.IsNullOrWhiteSpace(id) && !stepIds.Add(id))
                        {
                            AddError(errors, idToken, "WF-001", $"Duplicate Step ID '{id}'", "workflow.steps", "Workflow");
                        }
                    }
                }

                // Second pass: Validate Steps
                foreach (var step in stepsArray)
                {
                    var stepId = step["stepId"]?.Value<string>() ?? "unknown";
                    var typeToken = step["stepType"];
                    
                    if (typeToken == null)
                    {
                        AddError(errors, step, "WF-002", "Step missing 'stepType'", $"workflow.steps[{stepId}]", "Workflow");
                        continue;
                    }

                    var stepType = typeToken.Value<string>();
                    var nextSteps = step["nextSteps"] as JObject;
                    var conditions = step["conditions"] as JObject;

                    // Step Type Rules
                    if (string.Equals(stepType, "Decision", StringComparison.OrdinalIgnoreCase))
                    {
                        if (conditions == null || !conditions.Properties().Any())
                        {
                            AddError(errors, step, "WF-003", "Decision step must have 'conditions'", $"workflow.steps[{stepId}]", "Workflow");
                        }
                        else
                        {
                            foreach (var prop in conditions.Properties())
                            {
                                var target = prop.Value.Value<string>();
                                if (target != "END" && !stepIds.Contains(target))
                                {
                                    AddError(errors, prop.Value, "WF-004", $"Condition target '{target}' does not exist", $"workflow.steps[{stepId}]", "Workflow");
                                }
                            }
                        }
                    }
                    else if (string.Equals(stepType, "End", StringComparison.OrdinalIgnoreCase))
                    {
                        if (nextSteps != null && nextSteps.Properties().Any())
                        {
                            AddError(errors, nextSteps, "WF-005", "End step should not have 'nextSteps'", $"workflow.steps[{stepId}]", "Workflow");
                        }
                    }
                    else // Command, HumanTask, SystemTask, Timer
                    {
                        if (nextSteps == null || !nextSteps.Properties().Any())
                        {
                            AddError(errors, step, "WF-006", $"Step type '{stepType}' requires 'nextSteps' (exit path)", $"workflow.steps[{stepId}]", "Workflow");
                        }
                        else
                        {
                            foreach (var prop in nextSteps.Properties())
                            {
                                var eventName = prop.Name;
                                var target = prop.Value.Value<string>();

                                if (eventName != "Default" && !declaredEvents.Contains(eventName))
                                {
                                    AddError(errors, prop, "WF-007", $"Step triggers unknown event '{eventName}'", $"workflow.steps[{stepId}]", "Workflow");
                                }

                                if (target != "END" && !stepIds.Contains(target))
                                {
                                    AddError(errors, prop.Value, "WF-008", $"NextStep target '{target}' does not exist", $"workflow.steps[{stepId}]", "Workflow");
                                }
                            }
                        }
                    }

                    // SLA Linting
                    var slaToken = step["sla"] as JObject;
                    if (slaToken != null)
                    {
                        var duration = slaToken["duration"]?.Value<string>();
                        var timeoutEvent = slaToken["timeoutEvent"]?.Value<string>();
                        var escalationStepId = slaToken["escalationStepId"]?.Value<string>();

                        if (string.IsNullOrWhiteSpace(duration))
                        {
                            AddError(errors, slaToken, "WF-012", $"Step '{stepId}' defines SLA without 'duration'", $"workflow.steps[{stepId}].sla", "Workflow");
                        }
                        if (string.IsNullOrWhiteSpace(timeoutEvent))
                        {
                            AddError(errors, slaToken, "WF-013", $"Step '{stepId}' defines SLA without 'timeoutEvent'", $"workflow.steps[{stepId}].sla", "Workflow");
                        }
                        else if (!declaredEvents.Contains(timeoutEvent))
                        {
                            AddError(errors, slaToken["timeoutEvent"] ?? slaToken, "WF-007", $"SLA timeout event '{timeoutEvent}' is not declared in events", $"workflow.steps[{stepId}].sla.timeoutEvent", "Workflow");
                        }

                        if (!string.IsNullOrEmpty(escalationStepId) && escalationStepId != "END" && !stepIds.Contains(escalationStepId))
                        {
                            AddError(errors, slaToken["escalationStepId"] ?? slaToken, "WF-008", $"SLA escalationStepId '{escalationStepId}' does not exist in steps", $"workflow.steps[{stepId}].sla.escalationStepId", "Workflow");
                        }
                    }
                }
            }

            // Start Step Validation
            var startStepIdToken = wfToken["startStepId"];
            if (startStepIdToken != null)
            {
                var startId = startStepIdToken.Value<string>();
                if (!stepIds.Contains(startId))
                {
                    AddError(errors, startStepIdToken, "WF-009", $"StartStepId '{startId}' does not exist", "workflow.startStepId", "Workflow");
                }
            }
            else
            {
                AddError(errors, wfToken, "WF-010", "Missing 'startStepId'", "workflow", "Workflow");
            }
        }

        private void ValidateRoles(JObject root, List<LintError> errors)
        {
            var capsToken = root["capabilities"];
            var declaredCaps = new HashSet<string>();

            if (capsToken is JArray capsArray)
            {
                foreach (var c in capsArray)
                {
                    var code = c["code"]?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(code)) declaredCaps.Add(code);
                }
            }

            var rolesToken = root["roles"];
            if (rolesToken is JArray rolesArray)
            {
                foreach (var role in rolesArray)
                {
                    var roleNameToken = role["name"];
                    var roleName = roleNameToken?.Value<string>();
                    var granted = role["grantedCapabilities"] as JArray;

                    if (granted != null)
                    {
                        foreach (var g in granted)
                        {
                            var capCode = g.Value<string>();
                            if (!string.IsNullOrWhiteSpace(capCode) && !declaredCaps.Contains(capCode))
                            {
                                // Optional warning: Undeclared capability usage
                                AddError(errors, g, "GOV-001", $"Role '{roleName}' uses undeclared capability '{capCode}'", "roles", "Governance");
                            }
                        }
                    }
                }
            }
        }

        private void ValidateReference(JToken parent, string propertyName, HashSet<string> validSet, string errorCode, string errorMsg, List<LintError> errors)
        {
            var token = parent[propertyName];
            if (token != null && token.Type == JTokenType.String)
            {
                var val = token.Value<string>();
                if (!validSet.Contains(val))
                {
                    AddError(errors, token, errorCode, $"{errorMsg}: '{val}'", propertyName, "Consistency");
                }
            }
        }

        private void AddError(List<LintError> errors, JToken token, string code, string message, string path, string category)
        {
            var lineInfo = token as IJsonLineInfo;
            int line = lineInfo?.LineNumber ?? 0;
            int col = lineInfo?.LinePosition ?? 0;
            errors.Add(new LintError(code, message, line, col, token.Path, category));
        }
    }
}
