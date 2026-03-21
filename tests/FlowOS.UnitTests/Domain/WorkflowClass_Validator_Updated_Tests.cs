using System;
using System.Collections.Generic;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using Xunit;

namespace FlowOS.UnitTests.Domain
{
    public class WorkflowClass_Validator_Updated_Tests
    {
        [Fact]
        public void Publish_InvalidPayloadSchema_ShouldThrow()
        {
            var invalidBp = new WorkflowClassBlueprint
            {
                Events = new List<EventBlueprint> 
                { 
                    new EventBlueprint 
                    { 
                        EventId = "EVT-BAD", 
                        Name = "Bad Schema", 
                        PayloadSchema = "{ invalid json }" 
                    } 
                },
                StateMachine = new StateMachineBlueprint { InitialState = "Start", States = new List<string> { "Start" } },
                Workflow = new WorkflowBlueprint
                {
                    StartStepId = "Start",
                    Steps = new List<StepBlueprint>
                    {
                        new StepBlueprint { StepId = "Start", StepType = "Command", NextSteps = new Dictionary<string, string> { { "Default", "END" } } }
                    }
                }
            };

            var wc = new WorkflowClass(Guid.NewGuid(), "BadSchemaWf", "1.0.0", invalidBp);
            var manager = new FlowOS.Domain.Services.WorkflowClassManager();
            var result = manager.Publish(wc);
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Code == "EVT-SCHEMA-001" || e.Message.Contains("EVT-SCHEMA-001"));
        }

        [Fact]
        public void Publish_ValidPayloadSchema_ShouldSucceed()
        {
            var validBp = new WorkflowClassBlueprint
            {
                Events = new List<EventBlueprint> 
                { 
                    new EventBlueprint 
                    { 
                        EventId = "EVT-GOOD", 
                        Name = "Good Schema", 
                        PayloadSchema = "{ \"type\": \"object\" }" 
                    } 
                },
                StateMachine = new StateMachineBlueprint { InitialState = "Start", States = new List<string> { "Start" } },
                Workflow = new WorkflowBlueprint
                {
                    StartStepId = "Start",
                    Steps = new List<StepBlueprint>
                    {
                        new StepBlueprint { StepId = "Start", StepType = "Command", NextSteps = new Dictionary<string, string> { { "Default", "END" } } }
                    }
                }
            };

            var wc = new WorkflowClass(Guid.NewGuid(), "GoodSchemaWf", "1.0.0", validBp);
            var manager = new FlowOS.Domain.Services.WorkflowClassManager();
            manager.Publish(wc);
            Assert.Equal(WorkflowClassStatus.Published, wc.Status);
        }

        [Fact]
        public void Publish_CommandStepWithoutNextSteps_ShouldThrow()
        {
            var invalidBp = new WorkflowClassBlueprint
            {
                StateMachine = new StateMachineBlueprint { InitialState = "Start", States = new List<string> { "Start" } },
                Workflow = new WorkflowBlueprint
                {
                    StartStepId = "Start",
                    Steps = new List<StepBlueprint>
                    {
                        new StepBlueprint 
                        { 
                            StepId = "Start", 
                            StepType = "Command", 
                            NextSteps = new Dictionary<string, string>() // Missing exit path
                        }
                    }
                }
            };

            var wc = new WorkflowClass(Guid.NewGuid(), "BadCommandWf", "1.0.0", invalidBp);
            var manager = new FlowOS.Domain.Services.WorkflowClassManager();
            var result = manager.Publish(wc);
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Message.Contains("WF-COMP-002") || e.Code.Contains("WF-COMP-002"));
        }

        [Fact]
        public void Publish_EndStepWithNextSteps_ShouldThrow()
        {
            var invalidBp = new WorkflowClassBlueprint
            {
                StateMachine = new StateMachineBlueprint { InitialState = "Start", States = new List<string> { "Start" } },
                Workflow = new WorkflowBlueprint
                {
                    StartStepId = "Start",
                    Steps = new List<StepBlueprint>
                    {
                        new StepBlueprint { StepId = "Start", StepType = "Command", NextSteps = new Dictionary<string, string> { { "Default", "Final" } } },
                        new StepBlueprint 
                        { 
                            StepId = "Final", 
                            StepType = "End", 
                            NextSteps = new Dictionary<string, string> { { "Default", "SomewhereElse" } } // End should not have next steps
                        }
                    }
                }
            };

            var wc = new WorkflowClass(Guid.NewGuid(), "BadEndWf", "1.0.0", invalidBp);
            var manager = new FlowOS.Domain.Services.WorkflowClassManager();
            var result = manager.Publish(wc);
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Message.Contains("WF-STRUCT-005") || e.Code.Contains("WF-STRUCT-005"));
        }

        [Fact]
        public void Publish_SystemTask_ShouldSucceed()
        {
            var validBp = new WorkflowClassBlueprint
            {
                StateMachine = new StateMachineBlueprint { InitialState = "Start", States = new List<string> { "Start" } },
                Workflow = new WorkflowBlueprint
                {
                    StartStepId = "Start",
                    Steps = new List<StepBlueprint>
                    {
                        new StepBlueprint 
                        { 
                            StepId = "Start", 
                            StepType = "SystemTask", 
                            NextSteps = new Dictionary<string, string> { { "Default", "END" } } 
                        }
                    }
                }
            };

            var wc = new WorkflowClass(Guid.NewGuid(), "SystemTaskWf", "1.0.0", validBp);
            var manager = new FlowOS.Domain.Services.WorkflowClassManager();
            manager.Publish(wc);
            Assert.Equal(WorkflowClassStatus.Published, wc.Status);
        }
    }
}
