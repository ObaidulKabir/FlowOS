using System.Text.Json;
using FlowOS.Agents.Abstractions;
using FlowOS.Agents.Implementations;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Services;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Domain.Services;
using FlowOS.Events.Models;
using FlowOS.Infrastructure.Services;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;

namespace FlowOS.UnitTests.Agents;

public class BoundedAutonomyTests
{
    [Fact]
    public void DecisionPacket_IncludesGuidelineBindingAndLegalEvents()
    {
        var tenantId = Guid.NewGuid();
        var definition = new WorkflowDefinition(tenantId, "Quote", 1, "ApproveQuote");
        definition.AddStep(new WorkflowStepDefinition("ApproveQuote", WorkflowStepType.HumanTask)
        {
            Actor = StepActor.Either,
            DecisionGuideline = "Accept if quote within 15% of estimate.",
            AllowedRoles = ["Advisor"],
            NextSteps = new Dictionary<string, string>
            {
                ["QUOTE_APPROVED"] = "Next",
                ["QUOTE_VOIDED"] = "Void",
                ["QUOTE_RESPONSE_OVERDUE"] = "Overdue"
            },
            Sla = new StepSlaDefinition("24h", "QUOTE_RESPONSE_OVERDUE", reminders: new List<StepReminderDefinition>
            {
                new("2h", "QUOTE_REMINDER_SENT")
            }),
            AutoCommit = new StepAutoCommitDefinition
            {
                MinConfidence = 0.9,
                AllowedEvents = ["QUOTE_APPROVED"]
            }
        });

        var instance = new WorkflowInstance(tenantId, definition.Id, Guid.NewGuid(), 1, "ApproveQuote", initialState: "Assigned");
        var sm = new StateMachineDefinition(tenantId, "Job", "Assigned");
        sm.AddState("Quoted");
        sm.AddTransition(new FlowOS.Domain.ValueObjects.StateTransition("Assigned", "Quoted", "QUOTE_APPROVED")
        {
            EventId = "QUOTE_APPROVED"
        });

        var canonical = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["Amount"] = JsonSerializer.SerializeToElement(1200)
        };

        var packet = DecisionPacketFactory.Create(
            instance,
            definition,
            sm,
            new List<DomainEvent>(),
            canonical,
            "ServiceRepair quotes above shop limit must request revision.",
            "Approve the quote");

        Assert.Equal("ApproveQuote", packet.CurrentStepId);
        Assert.Equal("Assigned", packet.CurrentState);
        Assert.Equal(StepActor.Either, packet.Actor);
        Assert.Contains("15%", packet.TemplateGuideline);
        Assert.Contains("shop limit", packet.PolicyGuideline);
        Assert.Equal(1200L, packet.CanonicalContext["Amount"]);
        Assert.Contains("QUOTE_APPROVED", packet.LegalNextStepEvents);
        Assert.Contains("QUOTE_VOIDED", packet.LegalNextStepEvents);
        Assert.Contains("QUOTE_APPROVED", packet.LegalStateMachineEvents);
        Assert.Equal("QUOTE_RESPONSE_OVERDUE", packet.TimeoutEvent);
        Assert.Contains(packet.SlaReminders, r => r.TriggerEvent == "QUOTE_REMINDER_SENT");
        Assert.Contains("QUOTE_APPROVED", packet.AutoCommit!.AllowedEvents);
        Assert.Equal("Accept if quote within 15% of estimate.", packet.Prompt.TemplateGuideline);
        Assert.Contains("shop limit", packet.Prompt.PolicyGuideline);
        Assert.Contains(packet.DeclaredTools, t => t.Kind == "event" && t.Name == "QUOTE_APPROVED");
    }

    [Fact]
    public void AgentContract_DropsIllegalNextStepsEvent()
    {
        var result = AgentResult.WithActions("ok", new List<SuggestedAction>
        {
            new("QUOTE_VOIDED", "void it", 0.99)
        });

        var filtered = AgentSuggestionContract.RestrictToLegalEvents(result, new[] { "QUOTE_APPROVED" });
        Assert.Empty(filtered.SuggestedActions);
    }

    [Fact]
    public async Task RiskAgent_WithDecisionPacket_OnlyReturnsLegalEvents()
    {
        var packet = new DecisionPacket(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ApproveQuote",
            "Assigned",
            "HumanTask",
            StepActor.Agent,
            "Accept in-bounds quotes.",
            null,
            new Dictionary<string, object?> { ["Amount"] = 6000 },
            new[] { "QUOTE_APPROVED" },
            new[] { "QUOTE_APPROVED" },
            new[] { "Advisor" },
            Array.Empty<SlaReminderFact>(),
            "QUOTE_RESPONSE_OVERDUE",
            new Dictionary<string, object>(),
            "Decide",
            new AutoCommitPolicy(0.9, new[] { "QUOTE_APPROVED" }));

        var result = await new RiskAnalysisAgent().ExecuteAsync(AgentContext.FromPacket(packet));
        Assert.Empty(result.SuggestedActions);
    }

    [Fact]
    public void AutoCommit_PublishesWhenConfidenceAndAllowedEventMatch()
    {
        var packet = PacketForPolicy(minConfidence: 0.9, allowed: "QUOTE_APPROVED", timeout: "QUOTE_RESPONSE_OVERDUE");
        var action = new SuggestedAction("QUOTE_APPROVED", "in bounds", 0.95);

        Assert.True(AutoCommitEvaluator.CanCommit(packet, action, out var reason));
        Assert.Equal("In bounds.", reason);
    }

    [Fact]
    public void AutoCommit_ParksLowConfidence()
    {
        var packet = PacketForPolicy(minConfidence: 0.9, allowed: "QUOTE_APPROVED", timeout: "QUOTE_RESPONSE_OVERDUE");
        var action = new SuggestedAction("QUOTE_APPROVED", "unsure", 0.4);

        Assert.False(AutoCommitEvaluator.CanCommit(packet, action, out var reason));
        Assert.Contains("minConfidence", reason);
    }

    [Fact]
    public void AutoCommit_RejectsTimeoutEvent()
    {
        var packet = PacketForPolicy(minConfidence: 0.5, allowed: "QUOTE_RESPONSE_OVERDUE", timeout: "QUOTE_RESPONSE_OVERDUE");
        var action = new SuggestedAction("QUOTE_RESPONSE_OVERDUE", "overdue", 0.99);

        Assert.False(AutoCommitEvaluator.CanCommit(packet, action, out var reason));
        Assert.Contains("timer-owned", reason);
    }

    [Fact]
    public void AutoCommit_RejectsEventOutsideNextSteps()
    {
        var packet = PacketForPolicy(minConfidence: 0.5, allowed: "QUOTE_VOIDED", timeout: "QUOTE_RESPONSE_OVERDUE");
        var action = new SuggestedAction("QUOTE_VOIDED", "void", 0.99);

        Assert.False(AutoCommitEvaluator.CanCommit(packet, action, out var reason));
        Assert.Contains("legal nextSteps", reason);
    }

    [Fact]
    public void Validator_RejectsAgentOnDefaultSkip_AndTimeoutAutoCommit()
    {
        var validator = new WorkflowClassValidator();
        var blueprint = new WorkflowClassBlueprint
        {
            Events =
            [
                new EventBlueprint { EventId = "QUOTE_APPROVED", Name = "Approved" },
                new EventBlueprint { EventId = "QUOTE_RESPONSE_OVERDUE", Name = "Overdue" }
            ],
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "ApproveQuote",
                Steps =
                [
                    new StepBlueprint
                    {
                        StepId = "ApproveQuote",
                        StepType = "HumanTask",
                        Actor = StepActor.Agent,
                        DecisionGuideline = "Decide quote.",
                        NextSteps = new Dictionary<string, string>
                        {
                            ["Default"] = "Next",
                            ["QUOTE_APPROVED"] = "Next",
                            ["QUOTE_RESPONSE_OVERDUE"] = "Overdue"
                        },
                        Sla = new StepSlaBlueprint
                        {
                            Duration = "24h",
                            TimeoutEvent = "QUOTE_RESPONSE_OVERDUE"
                        },
                        AutoCommit = new StepAutoCommitBlueprint
                        {
                            MinConfidence = 0.9,
                            AllowedEvents = ["QUOTE_RESPONSE_OVERDUE", "MISSING_EVENT"]
                        }
                    }
                ]
            }
        };

        var result = validator.Validate(blueprint);
        Assert.Contains(result.Errors, e => e.Code == "WF-AGENT-005");
        Assert.Contains(result.Errors, e => e.Code == "WF-AGENT-003");
        Assert.Contains(result.Errors, e => e.Code == "WF-AGENT-002");
    }

    [Fact]
    public void Validator_AcceptsAgentPromptAliasWithoutDecisionGuideline()
    {
        var validator = new WorkflowClassValidator();
        var blueprint = new WorkflowClassBlueprint
        {
            Events = [new EventBlueprint { EventId = "QUOTE_APPROVED", Name = "Approved" }],
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "ApproveQuote",
                Steps =
                [
                    new StepBlueprint
                    {
                        StepId = "ApproveQuote",
                        StepType = "HumanTask",
                        Actor = StepActor.Agent,
                        AgentPrompt = "quote-approval",
                        NextSteps = new Dictionary<string, string> { ["QUOTE_APPROVED"] = "END" }
                    }
                ]
            }
        };

        var result = validator.Validate(blueprint);
        Assert.DoesNotContain(result.Errors, e => e.Code == "WF-AGENT-004");
    }

    [Fact]
    public void Compiler_MapsActorGuidelineAutoCommitAndBindingPolicyEvents()
    {
        var tenantId = Guid.NewGuid();
        var source = new WorkflowClass(
            tenantId,
            "ReusableApproval",
            "1.0.0",
            new WorkflowClassBlueprint
            {
                Events = [new EventBlueprint { EventId = "EVT-APPROVE", Name = "Approve", PayloadSchema = """{"type":"object"}""" }],
                StateMachine = new StateMachineBlueprint
                {
                    EntityType = "ApprovalSubject",
                    InitialState = "Pending",
                    States = ["Pending", "Approved"],
                    Transitions =
                    [
                        new TransitionBlueprint { FromState = "Pending", ToState = "Approved", EventId = "EVT-APPROVE" }
                    ]
                },
                Workflow = new WorkflowBlueprint
                {
                    StartStepId = "Review",
                    Steps =
                    [
                        new StepBlueprint
                        {
                            StepId = "Review",
                            StepType = "HumanTask",
                            Actor = StepActor.Either,
                            DecisionGuideline = "Approve low risk.",
                            RequiredRoles = ["Approver"],
                            NextSteps = new Dictionary<string, string> { ["EVT-APPROVE"] = "END" },
                            AutoCommit = new StepAutoCommitBlueprint
                            {
                                MinConfidence = 0.91,
                                AllowedEvents = ["EVT-APPROVE"]
                            },
                            AgentProvider = "quote-llm",
                            AgentPrompt = "quote-approval",
                            AgentTools = ["Webhook", "capability:payment.refund.v1"]
                        }
                    ]
                },
                Roles = [new RoleBlueprint { Name = "Approver" }],
                Capabilities = [new CapabilityBlueprint { Code = "event.publish.EVT-APPROVE" }]
            });

        var binding = new WorkflowContextBinding(Guid.NewGuid(), "Expense", "ExpenseApproval");
        var revision = new WorkflowContextBindingRevision(
            binding.Id,
            1,
            source.Id,
            "1.0.0",
            new FlowOS.Domain.ValueObjects.WorkflowContextBindingDefinition
            {
                EntityType = "ExpenseEntity",
                EventAliases = new Dictionary<string, string> { ["EVT-APPROVE"] = "EVT-EXP-APPROVE" },
                PolicyGuideline = "Expense overlay: never auto-approve travel."
            });

        var package = WorkflowClassCompiler.MapToContextRuntimePackage(source, binding, revision);
        var review = package.WorkflowDefinition.Steps.Single(x => x.StepId == "Review");

        Assert.Equal(StepActor.Either, review.Actor);
        Assert.Equal("Approve low risk.", review.DecisionGuideline);
        Assert.Equal(0.91, review.AutoCommit!.MinConfidence);
        Assert.Contains("EVT-EXP-APPROVE", review.AutoCommit.AllowedEvents);
        Assert.Equal("quote-llm", review.AgentProvider);
        Assert.Equal("quote-approval", review.AgentPrompt);
        Assert.Contains("Webhook", review.AgentTools);
        Assert.Contains("capability:payment.refund.v1", review.AgentTools);
        Assert.Equal("Expense overlay: never auto-approve travel.", revision.Definition.PolicyGuideline);

        var runtime = WorkflowClassCompiler.MapToRuntimeDefinition(source);
        Assert.Equal(StepActor.Either, runtime.Steps.Single().Actor);
        Assert.Equal("quote-llm", runtime.Steps.Single().AgentProvider);
        Assert.Equal("quote-approval", runtime.Steps.Single().AgentPrompt);
        Assert.Contains("Webhook", runtime.Steps.Single().AgentTools);
    }

    [Fact]
    public void AgentToolCatalog_IncludesLegalEventsAndResolvesDeclaredPlugins()
    {
        var tools = AgentToolCatalog.FromStep(
            new[] { "QUOTE_APPROVED" },
            new[] { "plugin:Webhook", "capability:payment.refund.v1" },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["webhook"] = "Webhook"
            });

        Assert.Contains(tools, t => t.Kind == "event" && t.Name == "QUOTE_APPROVED");
        var plugin = Assert.Single(tools, t => t.Kind == "plugin");
        Assert.Equal("plugin:Webhook", plugin.Name);
        Assert.Equal("Webhook", plugin.Provider);
        Assert.Contains(tools, t => t.Kind == "capability" && t.Name == "capability:payment.refund.v1");
    }

    [Fact]
    public void AgentToolCatalog_ParsesResourcePluginsAndDoesNotPrefetchWrites()
    {
        var tools = AgentToolCatalog.FromStep(
            Array.Empty<string>(),
            new[]
            {
                "LookupRecord:crm.customer.get.v1",
                "QueryRecords:inventory.parts.query.v1",
                "FetchDocument:docs.quote.get.v1",
                "SearchKnowledge:kb.policy.search.v1",
                "CheckPolicy:policy.approval-limit.v1",
                "capability:payment.refund.v1"
            });

        var lookup = Assert.Single(tools, t => t.Provider == "LookupRecord");
        Assert.Equal("resource", lookup.Kind);
        Assert.Equal("crm.customer.get.v1", lookup.Capability);
        Assert.True(lookup.Prefetch);
        Assert.Equal("none", lookup.SideEffect);
        Assert.Contains(tools, t => t.Provider == "QueryRecords" && t.Prefetch);
        Assert.Contains(tools, t => t.Provider == "FetchDocument");
        Assert.Contains(tools, t => t.Provider == "SearchKnowledge");
        Assert.Contains(tools, t => t.Provider == "CheckPolicy");
        var write = Assert.Single(tools, t => t.Name == "capability:payment.refund.v1");
        Assert.Equal("write", write.SideEffect);
        Assert.False(write.Prefetch);
    }

    [Fact]
    public void PreviewFromClass_ComposesAgentContextForWaitingStep()
    {
        var tenantId = Guid.NewGuid();
        var workflowClass = new WorkflowClass(tenantId, "Quote", "1.0.0", new WorkflowClassBlueprint
        {
            Events = [new EventBlueprint { EventId = "QUOTE_APPROVED", Name = "Approve quote" }],
            StateMachine = new StateMachineBlueprint
            {
                EntityType = "ServiceRepair",
                InitialState = "Assigned",
                States = ["Assigned", "Quoted"],
                Transitions =
                [
                    new TransitionBlueprint { FromState = "Assigned", ToState = "Quoted", EventId = "QUOTE_APPROVED" }
                ]
            },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "ApproveQuote",
                Steps =
                [
                    new StepBlueprint
                    {
                        StepId = "ApproveQuote",
                        StepType = "HumanTask",
                        Actor = StepActor.Either,
                        DecisionGuideline = "Accept if quote within 15%.",
                        AgentPrompt = "quote-approval",
                        AgentProvider = "quote-llm",
                        AgentTools = ["LookupRecord:crm.customer.get.v1", "CheckPolicy:policy.approval-limit.v1"],
                        RequiredRoles = ["ServiceAdvisor"],
                        NextSteps = new Dictionary<string, string>
                        {
                            ["QUOTE_APPROVED"] = "MaterialDecision",
                            ["QUOTE_REVISION_REQUESTED"] = "ReviseQuote"
                        },
                        AutoCommit = new StepAutoCommitBlueprint
                        {
                            MinConfidence = 0.9,
                            AllowedEvents = ["QUOTE_APPROVED"]
                        }
                    }
                ]
            },
            Roles = [new RoleBlueprint { Name = "ServiceAdvisor" }],
            Capabilities = [new CapabilityBlueprint { Code = "event.publish.QUOTE_APPROVED" }]
        });

        var packet = DecisionPacketFactory.PreviewFromClass(
            workflowClass,
            "ApproveQuote",
            "Shop overlay: never auto-approve above approvalLimit.",
            new Dictionary<string, object?> { ["Amount"] = 4800L },
            "Assigned");

        Assert.Equal("ApproveQuote", packet.CurrentStepId);
        Assert.Equal("Assigned", packet.CurrentState);
        Assert.Contains("QUOTE_APPROVED", packet.LegalNextStepEvents);
        Assert.Contains("QUOTE_APPROVED", packet.LegalStateMachineEvents);
        Assert.Equal(4800L, packet.CanonicalContext["Amount"]);
        Assert.Equal("Shop overlay: never auto-approve above approvalLimit.", packet.PolicyGuideline);
        Assert.Contains(packet.DeclaredTools, t => t.Name == "LookupRecord:crm.customer.get.v1" && t.Prefetch);

        var composed = AgentContextComposer.FromPacket(packet, "preview", workflowClass.Id, hideInstance: true);
        var json = JsonSerializer.Serialize(composed);
        Assert.Contains("\"mode\":\"preview\"", json);
        Assert.Contains("prompt", json);
        Assert.Contains("canonicalContext", json);
        Assert.Contains("LookupRecord:crm.customer.get.v1", json);
    }

    [Fact]
    public void AgentContextComposer_OmitsApiKeyAndExposesFourParts()
    {
        var tools = AgentToolCatalog.FromStep(
            new[] { "QUOTE_APPROVED" },
            new[] { "LookupRecord:crm.customer.get.v1" });
        var packet = PacketForPolicy(0.9, "QUOTE_APPROVED", "QUOTE_RESPONSE_OVERDUE") with
        {
            Tools = tools,
            PromptBinding = new AgentPromptRef("quote-approval", "Quote approval", "System", "Approve in-band quotes."),
            Provider = new AgentProviderRef("quote-llm", "openai", "gpt-4o-mini", null, true),
            ToolResults = new Dictionary<string, object?>
            {
                ["LookupRecord:crm.customer.get.v1"] = new { ok = true, data = new { name = "Rafiq Motors" } }
            }
        };

        var composed = AgentContextComposer.FromPacket(packet, "live", prefetched: true);
        var json = JsonSerializer.Serialize(composed);
        Assert.DoesNotContain("\"apiKey\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("quote-approval", json);
        Assert.Contains("hasApiKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LookupRecord:crm.customer.get.v1", json);
        Assert.Contains("Rafiq Motors", json);
    }

    [Fact]
    public async Task AgentToolHost_PrefetchesDeclaredReadToolsThroughInvoker()
    {
        var invoker = new FakeCapabilityInvoker();
        var host = new AgentToolHost(
            new IAgentResourcePlugin[] { new LookupRecordResourcePlugin(invoker) },
            invoker);

        var tools = AgentToolCatalog.FromStep(
            Array.Empty<string>(),
            new[] { "LookupRecord:crm.customer.get.v1", "capability:payment.refund.v1" });

        var packet = PacketForPolicy(0.9, "QUOTE_APPROVED", "QUOTE_RESPONSE_OVERDUE") with { Tools = tools };
        var enriched = await host.PrefetchAsync(packet);

        Assert.True(enriched.ToolResults!.ContainsKey("LookupRecord:crm.customer.get.v1"));
        Assert.False(enriched.ToolResults.ContainsKey("capability:payment.refund.v1"));
        Assert.Equal(1, invoker.Calls);
        Assert.Equal("lookup", invoker.LastOperation);
        Assert.Equal("crm.customer.get.v1", invoker.LastCapability);
    }

    private sealed class FakeCapabilityInvoker : ICapabilityInvoker
    {
        public int Calls { get; private set; }
        public string? LastOperation { get; private set; }
        public string? LastCapability { get; private set; }

        public Task<CapabilityInvokeResult> InvokeAsync(
            Guid tenantId,
            string capabilityName,
            string operation,
            object? payload,
            Guid? workflowInstanceId = null,
            string? stepId = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastOperation = operation;
            LastCapability = capabilityName;
            return Task.FromResult(new CapabilityInvokeResult(
                true,
                200,
                """{"approvalLimit":5000}""",
                new Dictionary<string, object> { ["approvalLimit"] = 5000L },
                null));
        }
    }

    private static DecisionPacket PacketForPolicy(double minConfidence, string allowed, string timeout) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ApproveQuote",
            "Assigned",
            "HumanTask",
            StepActor.Agent,
            "guideline",
            null,
            new Dictionary<string, object?>(),
            new[] { "QUOTE_APPROVED" },
            new[] { "QUOTE_APPROVED" },
            Array.Empty<string>(),
            new[] { new SlaReminderFact("2h", "QUOTE_REMINDER_SENT") },
            timeout,
            new Dictionary<string, object>(),
            "Decide",
            new AutoCommitPolicy(minConfidence, new[] { allowed }));
}
