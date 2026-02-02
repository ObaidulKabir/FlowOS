using System;
using System.Collections.Generic;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using Xunit;

namespace FlowOS.UnitTests.Domain
{
    public class WorkflowClass_Validation_Tests
    {
        [Fact]
        public void Publish_IncompleteWorkflow_ShouldThrowValidationException()
        {
            // Arrange
            var invalidBp = new WorkflowClassBlueprint
            {
                Workflow = new WorkflowBlueprint
                {
                    Steps = new List<StepBlueprint>
                    {
                        new StepBlueprint 
                        { 
                            StepId = "", // Invalid: Empty ID
                            StepType = "Command",
                            NextSteps = new Dictionary<string, string>()
                        }
                    }
                }
            };

            var wc = new WorkflowClass(Guid.NewGuid(), "BadWorkflow", "1.0.0", invalidBp);

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() => wc.Publish());
            Assert.Contains("Validation failed", ex.Message);
        }
        [Fact]
        public void Publish_DecoupledWorkflow_ShouldSucceed()
        {
            // Arrange
            var validBp = new WorkflowClassBlueprint
            {
                StateMachine = new StateMachineBlueprint 
                { 
                    InitialState = "Pending", 
                    States = new List<string> { "Pending", "Active" } 
                },
                Workflow = new WorkflowBlueprint
                {
                    StartStepId = "DoTask", // DIFFERENT from SM.InitialState
                    Steps = new List<StepBlueprint>
                    {
                        new StepBlueprint 
                        { 
                            StepId = "DoTask", 
                            StepType = "Command",
                            NextSteps = new Dictionary<string, string> { { "Default", "END" } }
                        }
                    }
                }
            };

            var wc = new WorkflowClass(Guid.NewGuid(), "DecoupledWorkflow", "1.0.0", validBp);

            // Act
            wc.Publish();

            // Assert
            Assert.Equal(WorkflowClassStatus.Published, wc.Status);
        }

        [Fact]
        public void Publish_MissingStartStepId_ShouldThrow()
        {
            var invalidBp = new WorkflowClassBlueprint
            {
                StateMachine = new StateMachineBlueprint { InitialState = "S1", States = new List<string> { "S1" } },
                Workflow = new WorkflowBlueprint
                {
                    StartStepId = "", // Missing
                    Steps = new List<StepBlueprint>
                    {
                        new StepBlueprint { StepId = "S1", StepType = "Command", NextSteps = new Dictionary<string, string> { { "Default", "END" } } }
                    }
                }
            };
            var wc = new WorkflowClass(Guid.NewGuid(), "BadWf", "1.0.0", invalidBp);
            var ex = Assert.Throws<InvalidOperationException>(() => wc.Publish());
            Assert.Contains("WF-COMP-000", ex.Message);
        }
    [Fact]
    public void Publish_WorkflowWithUnreachableSteps_ShouldThrow()
    {
        var invalidBp = new WorkflowClassBlueprint
        {
            StateMachine = new StateMachineBlueprint { InitialState = "Start", States = new List<string> { "Start" } },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "Start",
                Steps = new List<StepBlueprint>
                {
                    new StepBlueprint { StepId = "Start", StepType = "Command", NextSteps = new Dictionary<string, string> { { "Default", "END" } } },
                    new StepBlueprint { StepId = "Unreachable", StepType = "Command", NextSteps = new Dictionary<string, string> { { "Default", "END" } } }
                }
            }
        };
        var wc = new WorkflowClass(Guid.NewGuid(), "BadWf", "1.0.0", invalidBp);
        var ex = Assert.Throws<InvalidOperationException>(() => wc.Publish());
        Assert.Contains("WF-COMP-004", ex.Message);
    }

    [Fact]
    public void Publish_WorkflowWithUnknownEvents_ShouldThrow()
    {
        var invalidBp = new WorkflowClassBlueprint
        {
            Events = new List<EventBlueprint> { new EventBlueprint { EventId = "EVT-KNOWN", Name = "Known" } },
            StateMachine = new StateMachineBlueprint { InitialState = "Start", States = new List<string> { "Start" } },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "Start",
                Steps = new List<StepBlueprint>
                {
                    new StepBlueprint { StepId = "Start", StepType = "Command", NextSteps = new Dictionary<string, string> { { "EVT-UNKNOWN", "END" } } }
                }
            }
        };
        var wc = new WorkflowClass(Guid.NewGuid(), "BadWf", "1.0.0", invalidBp);
        var ex = Assert.Throws<InvalidOperationException>(() => wc.Publish());
        Assert.Contains("CON-005", ex.Message);
    }

    [Fact]
    public void Publish_StateMachineWithInvalidTransitions_ShouldThrow()
    {
        var invalidBp = new WorkflowClassBlueprint
        {
            StateMachine = new StateMachineBlueprint
            {
                InitialState = "S1",
                States = new List<string> { "S1", "S2" },
                Transitions = new List<TransitionBlueprint>
                {
                    new TransitionBlueprint { FromState = "S1", ToState = "S3", EventId = "EVT-1" } // S3 is unknown
                }
            },
            Workflow = new WorkflowBlueprint { StartStepId = "Start", Steps = new List<StepBlueprint> { new StepBlueprint { StepId = "Start", StepType = "Command" } } }
        };
        var wc = new WorkflowClass(Guid.NewGuid(), "BadWf", "1.0.0", invalidBp);
        var ex = Assert.Throws<InvalidOperationException>(() => wc.Publish());
        Assert.Contains("CON-003", ex.Message);
    }

    [Fact]
    public void RegressionGuard_StateNamesNotMatchingStepIds_ShouldSucceed()
    {
        // Explicitly proving that States and Steps are separate namespaces
        var validBp = new WorkflowClassBlueprint
        {
            StateMachine = new StateMachineBlueprint { InitialState = "StateA", States = new List<string> { "StateA", "StateB" } },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "Step1", // Totally different name
                Steps = new List<StepBlueprint>
                {
                    new StepBlueprint { StepId = "Step1", StepType = "Command", NextSteps = new Dictionary<string, string> { { "Default", "END" } } }
                }
            }
        };
        var wc = new WorkflowClass(Guid.NewGuid(), "DecoupledWf", "1.0.0", validBp);
        wc.Publish();
        Assert.Equal(WorkflowClassStatus.Published, wc.Status);
    }
}
}
