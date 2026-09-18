using System;
using System.Collections.Generic;
using FlowOS.Workflows.Engine;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;
using FlowOS.StateMachines.Engine;
using FlowOS.StateMachines.Models;
using FlowOS.Events.Models;
using ExecutionContext = FlowOS.StateMachines.Models.ExecutionContext;

namespace SimTest
{
    class Program
    {
        static void Main()
        {
            var tenantId = Guid.NewGuid();
            var wfDefId = Guid.NewGuid();
            var wfClassId = Guid.NewGuid();

            var smEngine = new StateMachineEngine();
            var engine = new WorkflowEngine(smEngine);

            var instance = new WorkflowInstance(
                tenantId,
                wfDefId,
                wfClassId,
                1,
                ""IntakeApplication"",
                null,
                ""Draft""
            );

            var smDef = new StateMachineDefinition
            {
                InitialState = ""Draft"",
                States = new List<string> { ""Draft"", ""Evaluating"", ""UnderwritingReview"", ""Approved"", ""Declined"" },
                Transitions = new List<TransitionDefinition>
                {
                    new TransitionDefinition { FromState = ""Draft"", ToState = ""Evaluating"", EventId = ""EVT-APPLY"" }
                }
            };

            var wfDef = new WorkflowDefinition
            {
                Id = wfDefId,
                Version = 1,
                StartStepId = ""IntakeApplication"",
                Steps = new List<WorkflowStepDefinition>
                {
                    new WorkflowStepDefinition
                    {
                        StepId = ""IntakeApplication"",
                        StepType = WorkflowStepType.Command,
                        NextSteps = new Dictionary<string, string> { { ""EVT-APPLY"", ""EvaluateRisk"" } }
                    },
                    new WorkflowStepDefinition
                    {
                        StepId = ""EvaluateRisk"",
                        StepType = WorkflowStepType.Decision,
                        Conditions = new Dictionary<string, string>
                        {
                            { ""CreditScore >= 720"", ""FastTrackDisbursement"" },
                            { ""Default"", ""UnderwriterReview"" }
                        }
                    },
                    new WorkflowStepDefinition
                    {
                        StepId = ""FastTrackDisbursement"",
                        StepType = WorkflowStepType.Command,
                        NextSteps = new Dictionary<string, string> { { ""EVT-AUTO-APPROVE"", ""DisburseFunds"" } }
                    }
                }
            };

            Console.WriteLine($""Before: Step={instance.CurrentStepId}, State={instance.CurrentState}"");

            var context = new ExecutionContext(new Dictionary<string, object> { { ""CreditScore"", 750 } }, new Dictionary<string, object>());

            var result = engine.Advance(instance, wfDef, new StandardEvent(tenantId, ""EVT-APPLY""), context, smDef, instance.CurrentState);

            Console.WriteLine($""After: Success={result.Success}, Step={instance.CurrentStepId}, State={instance.CurrentState}, ReturnStep={result.NextStepId}"");
        }
    }
}
