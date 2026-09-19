using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.DTOs.Governance;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Services;
using FlowOS.Domain.Validation;

namespace FlowOS.Application.Services;

public class WorkflowCopilotService : IWorkflowCopilotService
{
    private readonly IWorkflowClassValidator _validator;

    public WorkflowCopilotService(IWorkflowClassValidator validator)
    {
        _validator = validator;
    }

    public Task<GenerateBlueprintCopilotResponse> GenerateBlueprintAsync(
        string prompt, 
        WorkflowClassBlueprint? currentBlueprint = null, 
        string mode = "create", 
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException("Prompt cannot be empty for Copilot generation.", nameof(prompt));
        }

        if (mode.Equals("refine", StringComparison.OrdinalIgnoreCase) && currentBlueprint != null)
        {
            return Task.FromResult(RefineExistingBlueprint(prompt, currentBlueprint));
        }

        if (mode.Equals("template", StringComparison.OrdinalIgnoreCase) ||
            mode.Equals("reusable-template", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(SynthesizeReusableTemplate(prompt));
        }

        return Task.FromResult(SynthesizeNewBlueprint(prompt));
    }

    private GenerateBlueprintCopilotResponse SynthesizeReusableTemplate(string prompt)
    {
        var generated = SynthesizeNewBlueprint(prompt);
        var blueprint = generated.Blueprint with
        {
            ContextSchema =
                """
                {"type":"object","properties":{"Amount":{"type":"number"},"Description":{"type":"string"},"RequestedBy":{"type":"string"}}}
                """,
            StateMachine = generated.Blueprint.StateMachine with
            {
                EntityType = "ApprovalSubject"
            }
        };
        WorkflowSimulationGovernance.Apply(blueprint);

        return generated with
        {
            SuggestedName = "ReusableApprovalTemplate",
            Summary = $"Synthesized reusable process template with {blueprint.StateMachine.States.Count} legal states and a canonical context schema.",
            Explanation = generated.Explanation +
                          "\n• **Reusable Template**: generic ApprovalSubject entity, canonical events/roles, and binding-ready context schema.",
            Blueprint = blueprint,
            Validation = _validator.Validate(blueprint)
        };
    }

    private GenerateBlueprintCopilotResponse SynthesizeNewBlueprint(string prompt)
    {
        var lowerPrompt = prompt.ToLowerInvariant();

        // 1. Domain Detection & Blueprint Naming
        string suggestedName = DetermineSuggestedName(lowerPrompt);
        string suggestedVersion = "1.0.0";

        // 2. Flags & Feature Intent Extraction
        bool hasParallel = Regex.IsMatch(lowerPrompt, @"\b(parallel|concurrent|simultaneous|scatter|fork|split|both)\b");
        bool hasDecision = Regex.IsMatch(lowerPrompt, @"\b(decision|rules?|risk|score|if|threshold|evaluate)\b");
        bool hasSla = Regex.IsMatch(lowerPrompt, @"\b(\d+\s*(h|hours?|d|days?|m|mins?)|sla|timeout|escalat\w*)\b");
        bool hasWebhook = Regex.IsMatch(lowerPrompt, @"\b(webhook|api|endpoint|http|rest)\b");
        bool hasNotification = Regex.IsMatch(lowerPrompt, @"\b(notif\w*|email|slack|teams|sms|alert|message)\b");
        bool hasCompensation = Regex.IsMatch(lowerPrompt, @"\b(compensat\w*|rollback|refund|revert|saga|failure)\b");
        bool hasTimer = Regex.IsMatch(lowerPrompt, @"\b(timer|delay|wait|expir\w*)\b");
        bool hasReminder = Regex.IsMatch(lowerPrompt, @"\b(reminder|alert|lead\s*time|countdown|before\s+event|after\s+event)\b");

        // Extract explicit SLA duration if present
        string slaDuration = "24h";
        var durationMatch = Regex.Match(lowerPrompt, @"\b(\d+)\s*(h|hours?|d|days?)\b");
        if (durationMatch.Success)
        {
            string val = durationMatch.Groups[1].Value;
            string unit = durationMatch.Groups[2].Value.StartsWith("d") ? "d" : "h";
            slaDuration = $"{val}{unit}";
        }

        // 3. Synthesize Events
        var events = new List<EventBlueprint>
        {
            new() { EventId = "EVT-SUBMIT", Name = "Submit For Processing", AllowedRoles = new List<string> { "User", "Employee" } },
            new() { EventId = "EVT-APPROVE", Name = "Approve Request", AllowedRoles = new List<string> { "Manager" } },
            new() { EventId = "EVT-REJECT", Name = "Reject Request", AllowedRoles = new List<string> { "Manager" } }
        };

        if (hasSla)
        {
            events.Add(new() { EventId = "EVT-ESCALATE", Name = "SLA Escalation Timeout" });
        }

        if (hasReminder)
        {
            events.Add(new() { EventId = "EVT-REMINDER", Name = "Task Reminder Alert" });
        }

        // 4. Synthesize State Machine
        var states = new List<string> { "Draft", "Reviewing", "Approved", "Rejected" };
        if (hasParallel)
        {
            states = new List<string> { "Draft", "InParallelVerification", "Approved", "Rejected" };
        }

        var transitions = new List<TransitionBlueprint>
        {
            new() { FromState = states[0], ToState = states[1], EventId = "EVT-SUBMIT" },
            new() { FromState = states[1], ToState = states[2], EventId = "EVT-APPROVE" },
            new() { FromState = states[1], ToState = states[3], EventId = "EVT-REJECT" }
        };

        if (hasSla)
        {
            transitions.Add(new() { FromState = states[1], ToState = states[3], EventId = "EVT-ESCALATE" });
        }

        if (hasReminder)
        {
            transitions.Add(new() { FromState = states[1], ToState = states[1], EventId = "EVT-REMINDER" });
        }

        var stateMachine = new StateMachineBlueprint
        {
            EntityType = suggestedName.Replace("Workflow", "").Replace("Flow", "") + "Entity",
            InitialState = states[0],
            States = states,
            Transitions = transitions
        };

        // 5. Synthesize Procedural Workflow Steps
        var steps = new List<StepBlueprint>();
        string startStepId = "SubmitStep";

        // Initial Submit Step
        var submitStep = new StepBlueprint
        {
            StepId = "SubmitStep",
            StepType = "Command",
            RequiredRoles = new List<string> { "User" },
            NextSteps = new Dictionary<string, string> { { "EVT-SUBMIT", hasParallel ? "ParallelFork" : (hasDecision ? "EvaluateDecision" : "ReviewStep") } }
        };

        if (hasNotification)
        {
            submitStep.OnEntry = new List<StepActionBlueprint>
            {
                new()
                {
                    ActionType = "Notification",
                    Target = "RequestorChannel",
                    Template = $"Your {suggestedName} submission has been received and queued for processing."
                }
            };
        }

        steps.Add(submitStep);

        // Branching / Fork Step if Parallel requested
        if (hasParallel)
        {
            string branchA = ExtractBranchName(lowerPrompt, 0, "VerificationBranchA");
            string branchB = ExtractBranchName(lowerPrompt, 1, "VerificationBranchB");

            var forkStep = new StepBlueprint
            {
                StepId = "ParallelFork",
                StepType = "Fork",
                RequiredRoles = new List<string> { "System" },
                Branches = new List<string> { branchA, branchB }
            };
            steps.Add(forkStep);

            // Branch A Step
            var stepA = new StepBlueprint
            {
                StepId = branchA,
                StepType = "Command",
                RequiredRoles = new List<string> { "System" },
                NextSteps = new Dictionary<string, string> { { "Default", "ParallelJoin" } }
            };
            if (hasWebhook)
            {
                stepA.OnEntry = new List<StepActionBlueprint>
                {
                    new()
                    {
                        ActionType = "Webhook",
                        Url = $"https://api.internal/v1/{branchA.ToLowerInvariant()}",
                        Template = $"Executing automated check for {branchA}",
                        PayloadMapping = new Dictionary<string, string> { { "step", $"\"{branchA}\"" }, { "status", "\"Initiated\"" } },
                        SignPayload = true
                    }
                };
                stepA.OnFailure = new List<StepActionBlueprint>
                {
                    new()
                    {
                        ActionType = "Notification",
                        Target = "AlertsChannel",
                        Template = $"Automated check for {branchA} failed."
                    }
                };
            }
            steps.Add(stepA);

            // Branch B Step
            var stepB = new StepBlueprint
            {
                StepId = branchB,
                StepType = "HumanTask",
                RequiredRoles = new List<string> { "Reviewer" },
                NextSteps = new Dictionary<string, string> { { "Default", "ParallelJoin" } },
                Sla = hasSla ? new StepSlaBlueprint { Duration = slaDuration, TimeoutEvent = "EVT-ESCALATE", EscalationStepId = "ParallelJoin" } : null
            };
            steps.Add(stepB);

            // Join Step
            var joinStep = new StepBlueprint
            {
                StepId = "ParallelJoin",
                StepType = "Join",
                RequiredRoles = new List<string> { "System" },
                JoinPolicy = "WaitAll",
                InboundSteps = new List<string> { branchA, branchB },
                NextSteps = new Dictionary<string, string> { { "Default", hasDecision ? "EvaluateDecision" : "ReviewStep" } }
            };
            steps.Add(joinStep);
        }

        // Decision Step if Decision requested
        if (hasDecision)
        {
            var decisionStep = new StepBlueprint
            {
                StepId = "EvaluateDecision",
                StepType = "Decision",
                RequiredRoles = new List<string> { "System" },
                Conditions = new Dictionary<string, string>
                {
                    { "RiskScore < 30", "ApproveStep" },
                    { "RiskScore >= 75", "RejectStep" },
                    { "Default", "ReviewStep" }
                }
            };
            steps.Add(decisionStep);
        }

        // Review HumanTask Step
        var reviewStep = new StepBlueprint
        {
            StepId = "ReviewStep",
            StepType = "HumanTask",
            RequiredRoles = new List<string> { "Manager" },
            NextSteps = new Dictionary<string, string>
            {
                { "EVT-APPROVE", hasTimer ? "HoldTimer" : "ApproveStep" },
                { "EVT-REJECT", "RejectStep" }
            }
        };

        if (hasSla)
        {
            reviewStep.Sla = new StepSlaBlueprint
            {
                Duration = slaDuration,
                TimeoutEvent = "EVT-ESCALATE",
                EscalationStepId = "RejectStep"
            };
        }

        if (hasReminder)
        {
            reviewStep.NextSteps["EVT-REMINDER"] = "ReviewStep";
            if (reviewStep.Sla == null)
            {
                reviewStep.Sla = new StepSlaBlueprint
                {
                    Duration = slaDuration,
                    TimeoutEvent = "EVT-ESCALATE",
                    EscalationStepId = "RejectStep"
                };
            }
            reviewStep.Sla.Reminders = new List<StepReminderBlueprint>
            {
                new() { Duration = "-2h", TriggerEvent = "EVT-REMINDER" }
            };
            reviewStep.OnEntry.Add(new StepActionBlueprint
            {
                ActionType = "Notification",
                Template = "Task reminder: ReviewStep requires prompt review."
            });
        }

        if (hasCompensation)
        {
            reviewStep.OnFailure = new List<StepActionBlueprint>
            {
                new()
                {
                    ActionType = "Webhook",
                    Url = "https://audit.internal/v1/compensation-rollback",
                    Template = "Workflow Review failed or rejected: triggering compensation rollback.",
                    PayloadMapping = new Dictionary<string, string> { { "action", "\"Rollback\"" }, { "timestamp", "\"UtcNow\"" } }
                }
            };
        }

        steps.Add(reviewStep);

        // Optional Timer Step
        if (hasTimer)
        {
            var timerStep = new StepBlueprint
            {
                StepId = "HoldTimer",
                StepType = "Timer",
                RequiredRoles = new List<string> { "System" },
                Sla = new StepSlaBlueprint { Duration = "1h", TimeoutEvent = "EVT-APPROVE" },
                NextSteps = new Dictionary<string, string> { { "EVT-APPROVE", "ApproveStep" } }
            };
            steps.Add(timerStep);
        }

        // Terminal Approve Step
        var approveStep = new StepBlueprint
        {
            StepId = "ApproveStep",
            StepType = "Command",
            RequiredRoles = new List<string> { "System" },
            NextSteps = new Dictionary<string, string> { { "Default", "END" } }
        };
        if (hasNotification || hasWebhook)
        {
            approveStep.OnEntry = new List<StepActionBlueprint>
            {
                new()
                {
                    ActionType = "Notification",
                    Target = "AuditTeam",
                    Template = $"{suggestedName} successfully approved and completed."
                }
            };
        }
        steps.Add(approveStep);

        // Terminal Reject Step
        var rejectStep = new StepBlueprint
        {
            StepId = "RejectStep",
            StepType = "Command",
            RequiredRoles = new List<string> { "System" },
            NextSteps = new Dictionary<string, string> { { "Default", "END" } }
        };
        if (hasCompensation)
        {
            rejectStep.OnEntry = new List<StepActionBlueprint>
            {
                new()
                {
                    ActionType = "Notification",
                    Target = "RequestorChannel",
                    Template = $"{suggestedName} was rejected. Automatic rollback compensation has been triggered."
                }
            };
        }
        steps.Add(rejectStep);

        // Assemble Final Blueprint
        var blueprint = new WorkflowClassBlueprint
        {
            Events = events,
            StateMachine = stateMachine,
            Workflow = new WorkflowBlueprint
            {
                StartStepId = startStepId,
                Steps = steps
            }
        };
        WorkflowSimulationGovernance.Apply(blueprint);

        // Validate Generated Blueprint
        var validation = _validator.Validate(blueprint);

        // Summary and Explanation
        string summary = $"Synthesized '{suggestedName}' with {states.Count} legal states, {events.Count} events, and {steps.Count} procedural steps.";
        var explanationParts = new List<string>
        {
            $"• **State Machine**: pure decoupled lifecycle [{string.Join(" ➔ ", states)}].",
            $"• **Procedural Work**: starting at '{startStepId}', routing through {steps.Count} steps."
        };

        if (hasParallel) explanationParts.Add($"• **Parallel Execution**: Fork step spawns concurrent branches with a WaitAll barrier synchronization Join.");
        if (hasDecision) explanationParts.Add($"• **Decision Rules**: Synchronous Dynamic LINQ condition evaluation.");
        if (hasSla) explanationParts.Add($"• **SLA Governance**: Strict {slaDuration} countdown timer with automatic escalation.");
        if (hasReminder) explanationParts.Add($"• **Reminders & Alerts**: Pre/post-event countdown reminders configured on step SLA with 'EVT-REMINDER'.");
        if (hasCompensation) explanationParts.Add($"• **Saga Rollback**: OnFailure compensation hooks dispatch rollback side-effects upon failure.");
        explanationParts.Add("• **Capability Gates**: HumanTask exits declare requiredCapabilities; roles are grant bags plus inbox labels.");

        return new GenerateBlueprintCopilotResponse
        {
            SuggestedName = suggestedName,
            SuggestedVersion = suggestedVersion,
            Summary = summary,
            Explanation = string.Join("\n", explanationParts),
            Blueprint = blueprint,
            Validation = validation
        };
    }

    private GenerateBlueprintCopilotResponse RefineExistingBlueprint(string prompt, WorkflowClassBlueprint current)
    {
        var lowerPrompt = prompt.ToLowerInvariant();
        var refined = CloneBlueprint(current);

        bool addedFeature = false;
        var explanationParts = new List<string>();

        // Refine 1: Add SLA Timer
        if (Regex.IsMatch(lowerPrompt, @"\b(sla|timeout|24h|48h|7d)\b"))
        {
            string duration = "24h";
            var m = Regex.Match(lowerPrompt, @"\b(\d+)\s*(h|hours?|d|days?)\b");
            if (m.Success) duration = $"{m.Groups[1].Value}{(m.Groups[2].Value.StartsWith("d") ? "d" : "h")}";

            // Find human task or pending review step
            var targetStep = refined.Workflow.Steps.FirstOrDefault(s => s.StepType == "HumanTask")
                             ?? refined.Workflow.Steps.FirstOrDefault(s => s.StepId != refined.Workflow.StartStepId);

            if (targetStep != null)
            {
                if (!refined.Events.Any(e => e.EventId == "EVT-ESCALATE"))
                {
                    refined.Events.Add(new EventBlueprint { EventId = "EVT-ESCALATE", Name = "SLA Escalation Timeout" });
                }
                targetStep.Sla = new StepSlaBlueprint
                {
                    Duration = duration,
                    TimeoutEvent = "EVT-ESCALATE",
                    EscalationStepId = targetStep.NextSteps.Values.FirstOrDefault() ?? "END"
                };
                addedFeature = true;
                explanationParts.Add($"• Added SLA timer ({duration}) with timeout escalation to step '{targetStep.StepId}'.");
            }
        }

        // Refine 2: Add Webhook or Notification Hook
        bool isCompensationOnlyWebhook = Regex.IsMatch(lowerPrompt, @"\b(compensat\w*|rollback)\b.*\bwebhook\b|\bwebhook\b.*\b(compensat\w*|rollback)\b");
        if (!isCompensationOnlyWebhook && Regex.IsMatch(lowerPrompt, @"\b(webhook|notify|notification|email|slack|audit)\b"))
        {
            var targetStep = refined.Workflow.Steps.LastOrDefault(s => s.NextSteps.Values.Contains("END")) 
                             ?? refined.Workflow.Steps.LastOrDefault();

            if (targetStep != null)
            {
                var isWebhook = lowerPrompt.Contains("webhook");
                targetStep.OnEntry.Add(new StepActionBlueprint
                {
                    ActionType = isWebhook ? "Webhook" : "Notification",
                    Url = isWebhook ? "https://api.internal/v1/event-hook" : null,
                    Target = isWebhook ? null : "AlertsChannel",
                    Template = "Copilot-refined lifecycle action executed.",
                    SignPayload = true
                });
                if (isWebhook && targetStep.OnFailure.Count == 0)
                {
                    targetStep.OnFailure.Add(new StepActionBlueprint
                    {
                        ActionType = "Notification",
                        Target = "AlertsChannel",
                        Template = $"Execution of '{targetStep.StepId}' failed. Alerting on-call."
                    });
                }
                addedFeature = true;
                explanationParts.Add($"• Added OnEntry {(isWebhook ? "Webhook" : "Notification")} hook to step '{targetStep.StepId}'.");
            }
        }

        // Refine 3: Add Compensation OnFailure Hook
        if (Regex.IsMatch(lowerPrompt, @"\b(compensat\w*|rollback|failure|refund)\b"))
        {
            var targetStep = refined.Workflow.Steps.FirstOrDefault(s => s.StepType == "HumanTask")
                             ?? refined.Workflow.Steps.FirstOrDefault(s => s.StepId != refined.Workflow.StartStepId)
                             ?? refined.Workflow.Steps.FirstOrDefault();
            if (targetStep != null)
            {
                targetStep.OnFailure.Add(new StepActionBlueprint
                {
                    ActionType = "Webhook",
                    Url = "https://api.internal/v1/compensate-revert",
                    Template = "Automatic compensation rollback triggered by Copilot.",
                    PayloadMapping = new Dictionary<string, string> { { "status", "\"Compensated\"" } }
                });
                addedFeature = true;
                explanationParts.Add($"• Attached OnFailure compensation rollback hook to step '{targetStep.StepId}'.");
            }
        }

        // Refine 4: Add Step Reminder / Pre-Event Alert
        if (Regex.IsMatch(lowerPrompt, @"\b(reminder|alert|lead\s*time|countdown)\b"))
        {
            var targetStep = refined.Workflow.Steps.FirstOrDefault(s => s.StepType == "HumanTask")
                             ?? refined.Workflow.Steps.FirstOrDefault(s => s.Sla != null);

            if (targetStep != null)
            {
                if (!refined.Events.Any(e => e.EventId == "EVT-REMINDER"))
                {
                    refined.Events.Add(new EventBlueprint { EventId = "EVT-REMINDER", Name = "Task Reminder Alert" });
                }

                if (targetStep.Sla == null)
                {
                    targetStep.Sla = new StepSlaBlueprint
                    {
                        Duration = "24h",
                        TimeoutEvent = "EVT-ESCALATE",
                        EscalationStepId = targetStep.NextSteps.Values.FirstOrDefault() ?? "END"
                    };
                    if (!refined.Events.Any(e => e.EventId == "EVT-ESCALATE"))
                    {
                        refined.Events.Add(new EventBlueprint { EventId = "EVT-ESCALATE", Name = "SLA Escalation Timeout" });
                    }
                }

                targetStep.Sla.Reminders.Add(new StepReminderBlueprint
                {
                    Duration = "-2h",
                    TriggerEvent = "EVT-REMINDER"
                });

                if (!targetStep.NextSteps.ContainsKey("EVT-REMINDER"))
                {
                    targetStep.NextSteps["EVT-REMINDER"] = targetStep.StepId;
                }

                addedFeature = true;
                explanationParts.Add($"• Added pre-deadline reminder (-2h before SLA) with event 'EVT-REMINDER' to step '{targetStep.StepId}'.");
            }
        }

        if (!addedFeature)
        {
            explanationParts.Add("• Analyzed prompt and verified existing blueprint structural consistency.");
        }

        WorkflowSimulationGovernance.Apply(refined);
        var validation = _validator.Validate(refined);

        return new GenerateBlueprintCopilotResponse
        {
            SuggestedName = "RefinedWorkflow",
            SuggestedVersion = "1.1.0",
            Summary = "Refined existing workflow blueprint according to prompt instructions.",
            Explanation = string.Join("\n", explanationParts),
            Blueprint = refined,
            Validation = validation
        };
    }

    private static string DetermineSuggestedName(string lowerPrompt)
    {
        if (lowerPrompt.Contains("claim") || lowerPrompt.Contains("insurance")) return "InsuranceClaimWorkflow";
        if (lowerPrompt.Contains("kyc") || lowerPrompt.Contains("compliance")) return "KYCComplianceVerification";
        if (lowerPrompt.Contains("loan") || lowerPrompt.Contains("underwrit")) return "CommercialLoanUnderwriting";
        if (lowerPrompt.Contains("onboard") || lowerPrompt.Contains("vendor")) return "VendorOnboardingFlow";
        if (lowerPrompt.Contains("order") || lowerPrompt.Contains("fulfill") || lowerPrompt.Contains("saga")) return "OrderFulfillmentSaga";
        if ((lowerPrompt.Contains("alert") && lowerPrompt.Contains("escalat")) ||
            lowerPrompt.Contains("incidentalert") ||
            lowerPrompt.Contains("on-call") ||
            lowerPrompt.Contains("oncall"))
            return "IncidentAlertEscalation";
        if ((lowerPrompt.Contains("quote") && (lowerPrompt.Contains("agent") || lowerPrompt.Contains("auto"))) ||
            lowerPrompt.Contains("quoteautoreview") ||
            lowerPrompt.Contains("bounded autonomy"))
            return "QuoteAutoReview";
        if (lowerPrompt.Contains("secops") || lowerPrompt.Contains("incident") || lowerPrompt.Contains("access")) return "SecOpsAccessGovernance";
        if (lowerPrompt.Contains("expense") || lowerPrompt.Contains("reimburse")) return "ExpenseReimbursementFlow";

        // Generate clean PascalCase name from prompt keywords
        var words = Regex.Matches(lowerPrompt, @"[a-z0-9]+")
                         .Select(m => char.ToUpperInvariant(m.Value[0]) + m.Value.Substring(1))
                         .Take(3);
        string synthesized = string.Concat(words);
        return string.IsNullOrWhiteSpace(synthesized) ? "AutomatedBusinessProcess" : synthesized + "Workflow";
    }

    private static string ExtractBranchName(string lowerPrompt, int index, string fallback)
    {
        var candidates = new List<string>();
        if (lowerPrompt.Contains("id") || lowerPrompt.Contains("identity") || lowerPrompt.Contains("biometric")) candidates.Add("IdentityVerification");
        if (lowerPrompt.Contains("financial") || lowerPrompt.Contains("credit") || lowerPrompt.Contains("score")) candidates.Add("FinancialRiskScoring");
        if (lowerPrompt.Contains("sanction") || lowerPrompt.Contains("watchlist") || lowerPrompt.Contains("pep")) candidates.Add("SanctionsScreening");
        if (lowerPrompt.Contains("medical") || lowerPrompt.Contains("health")) candidates.Add("MedicalAssessment");
        if (lowerPrompt.Contains("vehicle") || lowerPrompt.Contains("car") || lowerPrompt.Contains("auto")) candidates.Add("VehicleDamageAssessment");
        if (lowerPrompt.Contains("fraud") || lowerPrompt.Contains("security")) candidates.Add("FraudPatternCheck");

        if (index < candidates.Count)
        {
            return candidates[index];
        }

        return fallback;
    }

    private static WorkflowClassBlueprint CloneBlueprint(WorkflowClassBlueprint source)
    {
        string json = System.Text.Json.JsonSerializer.Serialize(source);
        return System.Text.Json.JsonSerializer.Deserialize<WorkflowClassBlueprint>(json) ?? new WorkflowClassBlueprint();
    }
}
