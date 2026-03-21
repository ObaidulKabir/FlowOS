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
            var manager = new FlowOS.Domain.Services.WorkflowClassManager();
            var result = manager.Publish(wc);
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Message.Contains("Step ID cannot be empty"));
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
            var manager = new FlowOS.Domain.Services.WorkflowClassManager();
            manager.Publish(wc);

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
            var manager = new FlowOS.Domain.Services.WorkflowClassManager();
            var result = manager.Publish(wc);
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Message.Contains("WF-COMP-000") || e.Code.Contains("WF-COMP-000"));
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
        var manager = new FlowOS.Domain.Services.WorkflowClassManager();
        var result = manager.Publish(wc);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("WF-COMP-004") || e.Code.Contains("WF-COMP-004"));
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
        var manager = new FlowOS.Domain.Services.WorkflowClassManager();
        var result = manager.Publish(wc);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("CON-005") || e.Code.Contains("CON-005"));
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
        var manager = new FlowOS.Domain.Services.WorkflowClassManager();
        var result = manager.Publish(wc);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("CON-003") || e.Code.Contains("CON-003"));
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
        var manager = new FlowOS.Domain.Services.WorkflowClassManager();
        manager.Publish(wc);
        Assert.Equal(WorkflowClassStatus.Published, wc.Status);
    }
    
    [Fact]
    public void CreateDraft_WithUnknownNextStep_ShouldReturnErrors()
    {
        // Arrange
        var validator = new FlowOS.Domain.Services.WorkflowClassValidator();
        var manager = new FlowOS.Domain.Services.WorkflowClassManager(validator);

        var invalidBp = new WorkflowClassBlueprint
        {
            StateMachine = new StateMachineBlueprint 
            { 
                InitialState = "Created", 
                States = new List<string> { "Created", "Working" } 
            },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "Created",
                Steps = new List<StepBlueprint>
                {
                    new StepBlueprint 
                    { 
                        StepId = "Created", 
                        StepType = "Command",
                        NextSteps = new Dictionary<string, string> { { "EVT-WORKING", "Working" } }
                    },
                    new StepBlueprint 
                    { 
                        StepId = "Working", 
                        StepType = "HumanTask",
                        NextSteps = new Dictionary<string, string> { { "EVT-FINISHED", "Finished" } } // "Finished" step is missing
                    }
                }
            },
            Events = new List<EventBlueprint> 
            { 
                new EventBlueprint { EventId = "EVT-WORKING" },
                new EventBlueprint { EventId = "EVT-FINISHED" }
            }
        };

        var wc = new WorkflowClass(Guid.NewGuid(), "BadDraft", "0.1.0", invalidBp);

        // Act
        var result = manager.CreateDraft(wc);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "CON-004" && e.Message.Contains("'Finished'"));
    }
}
}
