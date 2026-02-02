using System;
using System.Collections.Generic;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using Xunit;

namespace FlowOS.UnitTests.Domain
{
    public class WorkflowClass_Governance_Tests
    {
        private WorkflowClass CreateValidDraft()
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
                            StepType = "Command", 
                            NextSteps = new Dictionary<string, string> { { "Default", "END" } } 
                        }
                    }
                }
            };
            return new WorkflowClass(Guid.NewGuid(), "TestWorkflow", "1.0.0", validBp);
        }

        [Fact]
        public void Delete_Draft_WithNoInstances_Succeeds()
        {
            var wc = CreateValidDraft();
            // Draft by default
            wc.Delete(hasInstances: false);
            // No exception thrown
        }

        [Fact]
        public void Delete_Draft_WithInstances_Fails()
        {
            var wc = CreateValidDraft();
            var ex = Assert.Throws<InvalidOperationException>(() => wc.Delete(hasInstances: true));
            Assert.Contains("GOV-DEL-002", ex.Message);
        }

        [Fact]
        public void Delete_Published_Fails()
        {
            var wc = CreateValidDraft();
            wc.Publish(); // Transition to Published
            
            var ex = Assert.Throws<InvalidOperationException>(() => wc.Delete(hasInstances: false));
            Assert.Contains("GOV-DEL-001", ex.Message);
        }

        [Fact]
        public void Abandon_Published_Succeeds()
        {
            var wc = CreateValidDraft();
            wc.Publish();
            
            wc.Abandon(wc.TenantId);
            
            Assert.Equal(WorkflowClassStatus.Abandoned, wc.Status);
        }

        [Fact]
        public void Abandon_Draft_Fails()
        {
            var wc = CreateValidDraft();
            var ex = Assert.Throws<InvalidOperationException>(() => wc.Abandon(wc.TenantId));
            Assert.Contains("Cannot abandon a Draft", ex.Message);
        }

        [Fact]
        public void Abandon_Public_ByTenant_Fails()
        {
            var wc = CreateValidDraft();
            wc.Publish();
            wc.SubmitForReview();
            wc.ApproveAsPublic(); // Scope is Public now
            
            var ex = Assert.Throws<InvalidOperationException>(() => wc.Abandon(Guid.NewGuid())); // Random tenant
            Assert.Contains("GOV-ABN-001", ex.Message);
        }

        [Fact]
        public void Abandon_Public_ByAdmin_Succeeds()
        {
            var wc = CreateValidDraft();
            wc.Publish();
            wc.SubmitForReview();
            wc.ApproveAsPublic();
            
            wc.Abandon(Guid.Empty); // Admin
            
            Assert.Equal(WorkflowClassStatus.Abandoned, wc.Status);
        }

        [Fact]
        public void CreateNewVersion_CreatesDraftWithNewVersion()
        {
            var wc = CreateValidDraft();
            wc.Publish();
            
            var newVersion = wc.CreateNewVersion("1.1.0");
            
            Assert.Equal(wc.Name, newVersion.Name);
            Assert.Equal("1.1.0", newVersion.Version);
            Assert.Equal(WorkflowClassStatus.Draft, newVersion.Status);
            Assert.Equal(WorkflowClassScope.Private, newVersion.Scope);
            Assert.Equal(wc.TenantId, newVersion.TenantId);
            Assert.Equal(wc.Definition, newVersion.Definition);
            Assert.Equal(wc.Id, newVersion.PreviousVersionId);
        }
        [Fact]
        public void CreateNewVersion_Immutability_RegressionGuard()
        {
            var wc = CreateValidDraft();
            wc.Publish();
            
            // Attempt to mutate published definition directly (simulated by new version)
            // Real immutability is enforced by the fact that CreateNewVersion returns a NEW object
            var v2 = wc.CreateNewVersion("1.1.0");
            
            Assert.NotSame(wc, v2);
            Assert.NotEqual(wc.Id, v2.Id);
            Assert.Equal(WorkflowClassStatus.Published, wc.Status); // Original stays Published
            Assert.Equal(WorkflowClassStatus.Draft, v2.Status); // New is Draft
        }
    }
}
