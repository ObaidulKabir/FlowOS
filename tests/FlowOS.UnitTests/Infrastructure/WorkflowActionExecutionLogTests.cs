using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Domain.Entities;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using FlowOS.MCP.Services;
using FlowOS.MCP.Tools;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FlowOS.UnitTests.Infrastructure;

public class WorkflowActionExecutionLogTests
{
    [Fact]
    public void WorkflowActionExecutionLog_Truncates_LongPayloads_And_SetsDefaults()
    {
        var tenantId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var hugePayload = new string('A', 3500);
        var hugeResponse = new string('B', 3000);

        var log = new WorkflowActionExecutionLog(
            tenantId: tenantId,
            workflowInstanceId: instanceId,
            stepId: "PaymentStep",
            triggerPhase: "OnEntry",
            actionType: "Webhook",
            target: "https://api.stripe.com/charges",
            status: "Succeeded",
            durationMs: 145,
            httpStatusCode: 200,
            requestPayloadSnippet: hugePayload,
            responseSnippet: hugeResponse,
            errorMessage: null,
            attemptNumber: 1,
            outboxMessageId: Guid.NewGuid()
        );

        Assert.NotEqual(Guid.Empty, log.Id);
        Assert.Equal(tenantId, log.TenantId);
        Assert.Equal(instanceId, log.WorkflowInstanceId);
        Assert.Equal("PaymentStep", log.StepId);
        Assert.Equal("OnEntry", log.TriggerPhase);
        Assert.Equal("Webhook", log.ActionType);
        Assert.Equal("https://api.stripe.com/charges", log.Target);
        Assert.Equal("Succeeded", log.Status);
        Assert.Equal(145, log.DurationMs);
        Assert.Equal(200, log.HttpStatusCode);
        Assert.StartsWith(new string('A', 2000), log.RequestPayloadSnippet!);
        Assert.EndsWith("... [truncated]", log.RequestPayloadSnippet!);
        Assert.StartsWith(new string('B', 2000), log.ResponseSnippet!);
        Assert.EndsWith("... [truncated]", log.ResponseSnippet!);
        Assert.Null(log.ErrorMessage);
    }

    [Fact]
    public async Task WorkflowActionHistoryService_CanFilter_And_EnforceTenantScoping()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FlowOSDbContext(options);
        var service = new WorkflowActionHistoryService(db);

        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();
        var instanceId = Guid.NewGuid();

        // Add logs for tenant1
        var log1 = new WorkflowActionExecutionLog(tenant1, instanceId, "Step1", "OnEntry", "Webhook", "https://api.example.com/1", "Succeeded", 120, 200);
        var log2 = new WorkflowActionExecutionLog(tenant1, instanceId, "Step1", "OnExit", "Notification", "OpsTeam", "Succeeded", 35);
        var log3 = new WorkflowActionExecutionLog(tenant1, instanceId, "Step2", "OnEntry", "Webhook", "https://api.example.com/fail", "Failed", 500, 500, errorMessage: "Server error");

        // Add log for tenant2
        var logOtherTenant = new WorkflowActionExecutionLog(tenant2, instanceId, "Step1", "OnEntry", "Webhook", "https://api.example.com/2", "Succeeded", 80, 200);

        db.ActionExecutionLogs.AddRange(log1, log2, log3, logOtherTenant);
        await db.SaveChangesAsync();

        // 1. Query all for tenant1
        var allT1 = await service.GetActionHistoryAsync(tenant1, instanceId);
        Assert.Equal(3, allT1.Count);

        // 2. Filter by stepId
        var step1Only = await service.GetActionHistoryAsync(tenant1, instanceId, stepId: "Step1");
        Assert.Equal(2, step1Only.Count);
        Assert.All(step1Only, l => Assert.Equal("Step1", l.StepId));

        // 3. Filter by status
        var failedOnly = await service.GetActionHistoryAsync(tenant1, instanceId, status: "Failed");
        Assert.Single(failedOnly);
        Assert.Equal("Step2", failedOnly[0].StepId);
        Assert.Equal("Server error", failedOnly[0].ErrorMessage);

        // 4. Query by ID
        var singleLog = await service.GetActionLogByIdAsync(log1.Id, tenant1);
        Assert.NotNull(singleLog);
        Assert.Equal(log1.Id, singleLog!.Id);

        // Tenant isolation: querying log1 with tenant2 returns null
        var isolated = await service.GetActionLogByIdAsync(log1.Id, tenant2);
        Assert.Null(isolated);
    }

    [Fact]
    public async Task ActionObservabilityMcpTools_Returns_Structured_History()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FlowOSDbContext(options);
        var historyService = new WorkflowActionHistoryService(db);
        var mcpTools = new ActionObservabilityMcpTools(historyService);

        var tenantId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();

        var log = new WorkflowActionExecutionLog(
            tenantId: tenantId,
            workflowInstanceId: instanceId,
            stepId: "ApprovalStep",
            triggerPhase: "OnEntry",
            actionType: "Webhook",
            target: "https://slack.com/api/chat.postMessage",
            status: "Succeeded",
            durationMs: 88,
            httpStatusCode: 200,
            requestPayloadSnippet: "{\"channel\":\"#approvals\"}",
            responseSnippet: "{\"ok\":true}"
        );
        db.ActionExecutionLogs.Add(log);
        await db.SaveChangesAsync();

        // Call MCP tool
        var args = new JObject
        {
            ["tenantId"] = tenantId.ToString(),
            ["workflowInstanceId"] = instanceId.ToString()
        };

        var result = await mcpTools.GetInstanceActionHistory(args);
        Assert.False(result.IsError);

        var json = JObject.Parse(result.Content[0].Text);
        Assert.True(json["ok"]?.Value<bool>());
        Assert.Equal(instanceId.ToString(), json["data"]?["workflowInstanceId"]?.ToString());
        Assert.Equal(1, json["data"]?["totalActions"]?.Value<int>());

        var actionElem = json["data"]?["actions"]?[0];
        Assert.NotNull(actionElem);
        Assert.Equal("ApprovalStep", actionElem!["stepId"]?.ToString());
        Assert.Equal("Webhook", actionElem["actionType"]?.ToString());
        Assert.Equal(88, actionElem["durationMs"]?.Value<long>());
        Assert.Equal(200, actionElem["httpStatusCode"]?.Value<int>());
        Assert.Equal("Succeeded", actionElem["status"]?.ToString());
    }

    [Fact]
    public async Task ActionObservabilityMcpTools_Rejects_Missing_WorkflowInstanceId()
    {
        var mockService = new Mock<IWorkflowActionHistoryService>();
        var mcpTools = new ActionObservabilityMcpTools(mockService.Object);

        var args = new JObject
        {
            ["tenantId"] = Guid.NewGuid().ToString()
            // workflowInstanceId omitted
        };

        var result = await mcpTools.GetInstanceActionHistory(args);
        Assert.True(result.IsError);
        var json = JObject.Parse(result.Content[0].Text);
        Assert.Equal("MCP-ARG-001", json["errorCode"]?.ToString());
    }

    [Fact]
    public async Task ExecutionTools_GetWorkflowHistory_Includes_Correlated_Actions()
    {
        var mediatorMock = new Mock<IMediator>();
        var historyServiceMock = new Mock<IWorkflowActionHistoryService>();

        var tenantId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();

        var detailDto = new FlowOS.Application.DTOs.Admin.AdminWorkflowDetailDto
        {
            Id = instanceId,
            DefinitionName = "InvoiceApproval",
            Version = 1,
            CurrentStepId = "Review",
            Status = "Running",
            CreatedAt = DateTime.UtcNow,
            Timeline = new List<FlowOS.Application.DTOs.Admin.AdminTimelineEventDto>()
        };

        mediatorMock
            .Setup(m => m.Send(It.IsAny<FlowOS.Application.Queries.Admin.GetAdminWorkflowDetailQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(detailDto);

        var actionLogDto = new WorkflowActionExecutionLogDto(
            Id: Guid.NewGuid(),
            TenantId: tenantId,
            WorkflowInstanceId: instanceId,
            StepId: "Review",
            TriggerPhase: "OnEntry",
            ActionType: "Webhook",
            Target: "https://api.example.com/notify",
            Status: "Succeeded",
            ExecutedAtUtc: DateTime.UtcNow,
            DurationMs: 95,
            HttpStatusCode: 200,
            RequestPayloadSnippet: "{}",
            ResponseSnippet: "{\"ok\":true}",
            ErrorMessage: null,
            AttemptNumber: 1,
            OutboxMessageId: null
        );

        historyServiceMock
            .Setup(s => s.GetActionHistoryAsync(tenantId, instanceId, null, null, null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkflowActionExecutionLogDto> { actionLogDto });

        var tools = new ExecutionTools(mediatorMock.Object, historyServiceMock.Object);

        var args = new JObject
        {
            ["tenantId"] = tenantId.ToString(),
            ["workflowInstanceId"] = instanceId.ToString()
        };

        var result = await tools.GetWorkflowHistory(args);
        Assert.False(result.IsError);

        var json = JObject.Parse(result.Content[0].Text);
        Assert.True(json["ok"]?.Value<bool>());
        Assert.NotNull(json["data"]?["actions"]);
        var actionsArray = json["data"]?["actions"] as JArray;
        Assert.NotNull(actionsArray);
        Assert.Single(actionsArray!);
        Assert.Equal("Review", actionsArray![0]["stepId"]?.ToString());
        Assert.Equal(95, actionsArray[0]["durationMs"]?.Value<long>());
    }
}
