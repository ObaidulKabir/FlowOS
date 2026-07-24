using System;
using System.Collections.Generic;
using FlowOS.Workflows.Engine;
using FlowOS.StateMachines.Engine;
using Xunit;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;
using FlowOS.Events.Models;
// Explicitly remove System.Threading to fix ambiguity if possible, or just alias
using ExecutionContext = FlowOS.StateMachines.Models.ExecutionContext;

namespace FlowOS.UnitTests.Engine
{
    public class PayloadEvaluationTests
    {
        private readonly WorkflowEngine _engine;
        private readonly Guid _tenantId = Guid.NewGuid();

        public PayloadEvaluationTests()
        {
            _engine = new WorkflowEngine(new StateMachineEngine());
        }

        private (WorkflowInstance, WorkflowDefinition) CreateScenario(string condition, string targetStep)
        {
            var def = new WorkflowDefinition(_tenantId, "TestWorkflow", 1);
            
            // Step 1: Start -> Decision
            var startStep = new WorkflowStepDefinition("Start", WorkflowStepType.SystemTask);
            startStep.NextSteps = new Dictionary<string, string> { { "Default", "DecisionPoint" } };
            def.Steps.Add(startStep);

            // Step 2: Decision
            var decisionStep = new WorkflowStepDefinition("DecisionPoint", WorkflowStepType.Decision);
            decisionStep.Conditions = new Dictionary<string, string>
            {
                { condition, targetStep }
            };
            decisionStep.NextSteps = new Dictionary<string, string>
            {
                { "Default", "FallbackStep" }
            };
            def.Steps.Add(decisionStep);

            // Step 3: Targets
            def.Steps.Add(new WorkflowStepDefinition(targetStep, WorkflowStepType.SystemTask));
            def.Steps.Add(new WorkflowStepDefinition("FallbackStep", WorkflowStepType.SystemTask));

            var instance = new WorkflowInstance(
                _tenantId, 
                def.Id, 
                Guid.Empty, 
                1, 
                "Start", // Start at "Start" to trigger the advance logic properly?
                // The tests expect Advance to move FROM current step.
                // If we start at "DecisionPoint", calling Advance with an event must trigger something.
                // The engine checks: if (!currentStep.NextSteps.TryGetValue(domainEvent.EventType...
                // Decision steps don't usually wait for events, they auto-execute.
                // BUT, WorkflowEngine.Advance is called when an EVENT happens.
                
                // If we are at "DecisionPoint" (Type=Decision), and we receive EVT-SUBMIT...
                // Does DecisionPoint have a transition for EVT-SUBMIT?
                // In my setup: NO. It has Conditions and NextSteps.
                
                // Wait, WorkflowEngine.Advance logic for Decision (lines 88+):
                // It is inside the "Advance" method.
                // Advance is called when an event occurs.
                // 1. It finds current step.
                // 2. It checks if event triggers transition: currentStep.NextSteps.TryGetValue(evt, out nextStepId)
                
                // ISSUE: Decision steps are usually "internal" flow steps. They shouldn't wait for an event.
                // They should be triggered automatically after the previous step.
                
                // However, for the UNIT TEST, we want to test the Decision Logic itself.
                // So we need to be at a step that transitions TO the decision step, 
                // OR we need to be AT the decision step and simulate the engine processing it?
                
                // The Engine.Advance method expects to move FROM CurrentStep TO NextStep via Event.
                // If NextStep is Decision, it executes the decision logic immediately (recursive/transient).
                
                // SO:
                // We should start at "Start".
                // "Start" has NextSteps: { "EVT-SUBMIT": "DecisionPoint" }
                // We fire "EVT-SUBMIT".
                // Engine moves Start -> DecisionPoint.
                // Engine sees DecisionPoint is Decision.
                // Engine executes Decision Logic.
                // Engine moves DecisionPoint -> Target (e.g. DirectorApproval).
                
                // Let's fix the setup to reflect this flow.
                // Start step needs to listen to EVT-SUBMIT and go to DecisionPoint.
                
                null // CorrelationId
            );
            
            // Fix Start Step transition
            startStep.NextSteps = new Dictionary<string, string> { { "EVT-SUBMIT", "DecisionPoint" }, { "EVT-ASSESS", "DecisionPoint" } };
            
            // Set instance to Start
            // But WorkflowInstance constructor sets CurrentStepId.
            // We need to set it to "Start".
            // The existing code sets it to "DecisionPoint". That was the bug.
            
            // Re-creating instance with correct start
             instance = new WorkflowInstance(
                _tenantId, 
                def.Id, 
                Guid.Empty, 
                1, 
                "Start", 
                null 
            );

            return (instance, def);
        }

        // Test Case 1: High Value Expense
        [Fact]
        public void Scenario1_HighValueExpense_ShouldRouteToDirector()
        {
            var (instance, def) = CreateScenario("Amount > 1000", "DirectorApproval");
            var context = new ExecutionContext 
            { 
                Payload = new Dictionary<string, object> { { "Amount", 1500 } } 
            };
            var evt = new StandardEvent(_tenantId, "EVT-SUBMIT");

            var result = _engine.Advance(instance, def, evt, context);

            Assert.True(result.Success);
            Assert.Equal("DirectorApproval", result.NewStepId);
        }

        // Test Case 2: Auto-Approval
        [Fact]
        public void Scenario2_SmallExpense_ShouldAutoApprove()
        {
            var (instance, def) = CreateScenario("Amount <= 50", "AutoApproved");
            var context = new ExecutionContext 
            { 
                Payload = new Dictionary<string, object> { { "Amount", 45.50 } } 
            };
            var evt = new StandardEvent(_tenantId, "EVT-SUBMIT");

            var result = _engine.Advance(instance, def, evt, context);

            Assert.True(result.Success);
            Assert.Equal("AutoApproved", result.NewStepId);
        }

        // Test Case 3: Category Routing (Equality)
        // Scenario: IT requests go to "ITQueue" (Equality check simulated with numbers for now due to engine limitation)
        // NOTE: The current engine implementation only supports numeric comparison in EvaluateCondition
        // Let's test the numeric equality support "=="
        [Fact]
        public void Scenario3_EqualityCheck_ShouldRouteCorrectly()
        {
            // Arrange
            // Using a numeric code for category: 1 = IT, 2 = HR
            var (instance, def) = CreateScenario("CategoryCode == 1", "ITQueue");
            var context = new ExecutionContext 
            { 
                Payload = new Dictionary<string, object> { { "CategoryCode", 1 } } 
            };
            var evt = new StandardEvent(_tenantId, "EVT-SUBMIT");

            // Act
            var result = _engine.Advance(instance, def, evt, context);

            // Assert
            Assert.True(result.Success);
            Assert.Equal("ITQueue", result.NewStepId);
        }

        // Test Case 4: Risk Assessment
        [Fact]
        public void Scenario4_HighRisk_ShouldTriggerAudit()
        {
            var (instance, def) = CreateScenario("RiskScore >= 80", "AuditTeam");
            var context = new ExecutionContext 
            { 
                Payload = new Dictionary<string, object> { { "RiskScore", 80 } } 
            };
            var evt = new StandardEvent(_tenantId, "EVT-ASSESS");

            var result = _engine.Advance(instance, def, evt, context);

            Assert.True(result.Success);
            Assert.Equal("AuditTeam", result.NewStepId);
        }

        // Test Case 5: Default Fallback
        [Fact]
        public void Scenario5_ConditionNotMet_ShouldUseFallback()
        {
            var (instance, def) = CreateScenario("Amount > 1000", "DirectorApproval");
            
            // Add Default key to Conditions for fallback logic to work as per Engine implementation
            // See WorkflowEngine.cs line 172: if (decisionTarget == null && nextStep.Conditions.ContainsKey("Default"))
            // My helper method puts it in NextSteps, but let's manually add it to Conditions here
            def.Steps[1].Conditions.Add("Default", "FallbackStep");

            var context = new ExecutionContext 
            { 
                Payload = new Dictionary<string, object> { { "Amount", 500 } } 
            };
            var evt = new StandardEvent(_tenantId, "EVT-SUBMIT");

            var result = _engine.Advance(instance, def, evt, context);

            Assert.True(result.Success);
            Assert.Equal("FallbackStep", result.NewStepId);
        }
    }
}
