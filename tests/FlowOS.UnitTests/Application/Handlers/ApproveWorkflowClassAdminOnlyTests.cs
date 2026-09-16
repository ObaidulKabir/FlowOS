using FlowOS.Application.Commands.Governance;
using FlowOS.Application.Handlers.Governance;
using FlowOS.Core.Interfaces;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Services;
using FlowOS.Domain.Validation;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FlowOS.UnitTests.Application.Handlers;

public class ApproveWorkflowClassAdminOnlyTests
{
    [Fact]
    public async Task ApproveAsPublic_RejectsNonAdminCaller()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new FlowOSDbContext(options);

        var wc = new WorkflowClass(Guid.NewGuid(), "PublicCandidate", "1.0.0", new WorkflowClassBlueprint
        {
            StateMachine = new StateMachineBlueprint
            {
                InitialState = "Start",
                States = new List<string> { "Start" }
            },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "Start",
                Steps = new List<StepBlueprint>
                {
                    new()
                    {
                        StepId = "Start",
                        StepType = "Command",
                        NextSteps = new Dictionary<string, string> { { "Default", "END" } }
                    }
                }
            }
        });
        var manager = new WorkflowClassManager();
        Assert.True(manager.Publish(wc).IsValid);
        Assert.True(manager.SubmitForReview(wc).IsValid);
        context.WorkflowClasses.Add(wc);
        await context.SaveChangesAsync();

        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(x => x.Roles).Returns(new List<string> { "User" });
        currentUser.Setup(x => x.TenantId).Returns(wc.TenantId);

        var handler = new WorkflowClassCommandHandlers(
            new UnitOfWork(context),
            manager,
            Mock.Of<IWorkflowClassVersionManager>(),
            Mock.Of<IWorkflowJsonLinter>(),
            currentUser.Object);

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new ApproveWorkflowClassCommand(wc.TenantId, wc.Id), CancellationToken.None));

        Assert.Contains("admin-only", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
