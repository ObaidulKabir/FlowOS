using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Api.Controllers;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Core.Interfaces;
using FlowOS.Security.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace FlowOS.UnitTests.Api;

/// <summary>
/// Tenant integration finding #1: REST PUT /api/plugin-bindings had no capability check at all for the
/// "agent" binding type (the MCP tools with the same gap are covered by AgentContextMcpToolsTests and
/// PluginBindingMcpToolsTests), and accepted a bare http:// endpoint - a BYO API key is sent to it as a
/// Bearer token - in every environment.
/// </summary>
public sealed class PluginBindingsControllerTests
{
    private static JsonElement AsConfigElement(object value) =>
        JsonSerializer.SerializeToElement(value);

    private static (
        PluginBindingsController Controller,
        Mock<IPluginBindingRegistryService> Bindings,
        Mock<ICapabilityService> Capabilities) CreateController(
        Guid tenantId,
        string environmentName = "Production",
        IConfiguration? configuration = null)
    {
        var bindings = new Mock<IPluginBindingRegistryService>();
        bindings
            .Setup(b => b.ListAsync(tenantId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PluginBindingDto>());

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.TenantId).Returns(tenantId);
        currentUser.SetupGet(u => u.Roles).Returns(new List<string> { "Admin" });
        currentUser.SetupGet(u => u.Scopes).Returns(Array.Empty<string>());
        currentUser.SetupGet(u => u.IsApiKey).Returns(false);

        var capabilities = new Mock<ICapabilityService>();

        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns(environmentName);

        var config = configuration ?? new ConfigurationBuilder().Build();

        var controller = new PluginBindingsController(
            bindings.Object,
            currentUser.Object,
            capabilities.Object,
            environment.Object,
            config);

        return (controller, bindings, capabilities);
    }

    [Fact]
    public async Task Upsert_AgentType_CallerLacksManageCapability_ReturnsForbidden()
    {
        var tenantId = Guid.NewGuid();
        var (controller, _, capabilities) = CreateController(tenantId);
        capabilities.Setup(c => c.GetCapabilitiesAsync(tenantId, It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new HashSet<string>());

        var request = new UpsertPluginBindingRequest
        {
            BindingType = "agent",
            SourceName = "quote-llm",
            ProviderName = "openai",
            Configuration = AsConfigElement(new { apiKey = "sk-should-not-be-stored" })
        };

        var result = await controller.Upsert(request, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Upsert_AgentType_CallerHasManageCapability_Succeeds()
    {
        var tenantId = Guid.NewGuid();
        var (controller, bindings, capabilities) = CreateController(tenantId, environmentName: "Development");
        capabilities.Setup(c => c.GetCapabilitiesAsync(tenantId, It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new HashSet<string> { AgentProviderKinds.ManageCapability });

        var binding = new PluginBindingDto(
            Guid.NewGuid(), tenantId, "agent", "quote-llm", "openai",
            true, DateTime.UtcNow, DateTime.UtcNow);
        bindings
            .Setup(b => b.UpsertAsync(
                tenantId, "agent", "quote-llm", "openai", true, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(binding);

        var request = new UpsertPluginBindingRequest
        {
            BindingType = "agent",
            SourceName = "quote-llm",
            ProviderName = "openai",
            Configuration = AsConfigElement(new { apiKey = "sk-test" })
        };

        var result = await controller.Upsert(request, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Upsert_AgentType_NonUrlEndpoint_ReturnsBadRequest()
    {
        var tenantId = Guid.NewGuid();
        var (controller, _, capabilities) = CreateController(tenantId, environmentName: "Development");
        capabilities.Setup(c => c.GetCapabilitiesAsync(tenantId, It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new HashSet<string> { AgentProviderKinds.ManageCapability });

        var request = new UpsertPluginBindingRequest
        {
            BindingType = "agent",
            SourceName = "quote-llm",
            ProviderName = "openai",
            Configuration = AsConfigElement(new { endpoint = "prospectbd2024@gmail.com" })
        };

        var result = await controller.Upsert(request, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("endpoint must be a valid HTTP or HTTPS URL", badRequest.Value?.ToString());
    }

    [Fact]
    public async Task Upsert_AgentType_PlainHttpEndpointOutsideDevelopment_ReturnsBadRequest()
    {
        var tenantId = Guid.NewGuid();
        var (controller, _, capabilities) = CreateController(tenantId, environmentName: "Production");
        capabilities.Setup(c => c.GetCapabilitiesAsync(tenantId, It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new HashSet<string> { AgentProviderKinds.ManageCapability });

        var request = new UpsertPluginBindingRequest
        {
            BindingType = "agent",
            SourceName = "quote-llm",
            ProviderName = "openai",
            Configuration = AsConfigElement(new { endpoint = "http://api.example.com/v1/chat/completions" })
        };

        var result = await controller.Upsert(request, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("https://", badRequest.Value?.ToString());
    }

    [Fact]
    public async Task Upsert_AgentType_PlainHttpEndpointInDevelopment_IsAllowed()
    {
        var tenantId = Guid.NewGuid();
        var (controller, bindings, capabilities) = CreateController(tenantId, environmentName: "Development");
        capabilities.Setup(c => c.GetCapabilitiesAsync(tenantId, It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new HashSet<string> { AgentProviderKinds.ManageCapability });

        var binding = new PluginBindingDto(
            Guid.NewGuid(), tenantId, "agent", "quote-llm", "openai",
            true, DateTime.UtcNow, DateTime.UtcNow);
        bindings
            .Setup(b => b.UpsertAsync(
                tenantId, "agent", "quote-llm", "openai", true, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(binding);

        var request = new UpsertPluginBindingRequest
        {
            BindingType = "agent",
            SourceName = "quote-llm",
            ProviderName = "openai",
            Configuration = AsConfigElement(new { endpoint = "http://localhost:11434/v1/chat/completions", apiKey = "sk-test" })
        };

        var result = await controller.Upsert(request, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task List_ApplicationId_ReturnsOnlyThatApplicationsBindings()
    {
        var tenantId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var (controller, bindings, _) = CreateController(tenantId);
        var shared = new PluginBindingDto(Guid.NewGuid(), tenantId, "prompt", "shared-prompt", "markdown",
            true, DateTime.UtcNow, DateTime.UtcNow, ApplicationId: null);
        var scoped = new PluginBindingDto(Guid.NewGuid(), tenantId, "prompt", "app-prompt", "markdown",
            true, DateTime.UtcNow, DateTime.UtcNow, ApplicationId: applicationId);
        bindings
            .Setup(b => b.ListAsync(tenantId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { shared, scoped });

        var result = Assert.IsType<OkObjectResult>(await controller.List(null, null, null, CancellationToken.None, applicationId, null));
        var json = JsonSerializer.SerializeToElement(result.Value!);
        Assert.Equal(1, json.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task List_Shared_ReturnsOnlyTenantSharedBindings()
    {
        var tenantId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var (controller, bindings, _) = CreateController(tenantId);
        var shared = new PluginBindingDto(Guid.NewGuid(), tenantId, "prompt", "shared-prompt", "markdown",
            true, DateTime.UtcNow, DateTime.UtcNow, ApplicationId: null);
        var scoped = new PluginBindingDto(Guid.NewGuid(), tenantId, "prompt", "app-prompt", "markdown",
            true, DateTime.UtcNow, DateTime.UtcNow, ApplicationId: applicationId);
        bindings
            .Setup(b => b.ListAsync(tenantId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { shared, scoped });

        var result = Assert.IsType<OkObjectResult>(await controller.List(null, null, null, CancellationToken.None, null, true));
        var json = JsonSerializer.SerializeToElement(result.Value!);
        Assert.Equal(1, json.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Delete_PromptOrProfileBinding_CallsDeleteScopedWithTheGivenScope()
    {
        var tenantId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var (controller, bindings, _) = CreateController(tenantId);
        bindings
            .Setup(b => b.DeleteScopedAsync(tenantId, applicationId, "prompt", "quote-approval", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await controller.Delete("prompt", "quote-approval", applicationId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Delete_UnknownScope_ReturnsNotFound()
    {
        var tenantId = Guid.NewGuid();
        var (controller, bindings, _) = CreateController(tenantId);
        bindings
            .Setup(b => b.DeleteScopedAsync(tenantId, null, "profile", "CreditUnderwriter", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await controller.Delete("profile", "CreditUnderwriter", null, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Delete_NonPromptOrProfileBindingType_ReturnsBadRequest()
    {
        var tenantId = Guid.NewGuid();
        var (controller, _, _) = CreateController(tenantId);

        var result = await controller.Delete("agent", "quote-llm", null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
