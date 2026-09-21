using FlowOS.Agents.Abstractions;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Infrastructure.Persistence.Repositories;
using FlowOS.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FlowOS.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the Unit of Work and repository abstractions used by the Application layer.
    /// </summary>
    public static IServiceCollection AddFlowOSPersistence(this IServiceCollection services)
    {
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IConfigurationPublisher, ConfigurationPublisher>();
        services.AddScoped<IWorkflowTimerService, WorkflowTimerService>();
        services.AddScoped<IWorkflowActionPlugin, WebhookWorkflowActionPlugin>();
        services.AddScoped<IWorkflowActionPlugin, NotificationWorkflowActionPlugin>();
        services.AddScoped<IWorkflowActionPlugin, PublishEventWorkflowActionPlugin>();
        services.AddScoped<IWorkflowActionPlugin, InvokeCapabilityWorkflowActionPlugin>();
        services.AddScoped<IWorkflowActionPlugin, SlackWorkflowActionPlugin>();
        services.AddScoped<IWorkflowActionPlugin, EmailWorkflowActionPlugin>();
        services.AddScoped<IWorkflowActionPlugin, WhatsAppWorkflowActionPlugin>();
        services.AddScoped<IWorkflowActionPlugin, GenericWorkflowActionPlugin>();
        services.AddScoped<IWorkflowActionPlugin, LookupRecordResourcePlugin>();
        services.AddScoped<IWorkflowActionPlugin, QueryRecordsResourcePlugin>();
        services.AddScoped<IWorkflowActionPlugin, FetchDocumentResourcePlugin>();
        services.AddScoped<IWorkflowActionPlugin, SearchKnowledgeResourcePlugin>();
        services.AddScoped<IWorkflowActionPlugin, CheckPolicyResourcePlugin>();
        services.AddScoped<IAgentResourcePlugin, LookupRecordResourcePlugin>();
        services.AddScoped<IAgentResourcePlugin, QueryRecordsResourcePlugin>();
        services.AddScoped<IAgentResourcePlugin, FetchDocumentResourcePlugin>();
        services.AddScoped<IAgentResourcePlugin, SearchKnowledgeResourcePlugin>();
        services.AddScoped<IAgentResourcePlugin, CheckPolicyResourcePlugin>();
        services.AddScoped<ICapabilityInvoker, CapabilityInvoker>();
        services.AddScoped<IAgentToolHost, FlowOS.Application.Services.AgentToolHost>();
        services.AddScoped<IWorkflowActionPluginRegistry, WorkflowActionPluginRegistry>();
        services.AddScoped<FlowOS.Infrastructure.Services.Communication.ISlackSender, FlowOS.Infrastructure.Services.Communication.DefaultSlackSender>();
        services.AddScoped<FlowOS.Infrastructure.Services.Communication.IEmailSender, FlowOS.Infrastructure.Services.Communication.DefaultEmailSender>();
        services.AddScoped<FlowOS.Infrastructure.Services.Communication.IWhatsAppSender, FlowOS.Infrastructure.Services.Communication.DefaultWhatsAppSender>();
        services.AddSingleton<FlowOS.Core.Common.Interfaces.IPolicyDecisionPlugin, DefaultPolicyDecisionPlugin>();
        services.AddSingleton<FlowOS.Core.Common.Interfaces.IPolicyDecisionPluginRegistry, PolicyDecisionPluginRegistry>();
        services.AddScoped<IWorkflowActionDispatcher, WorkflowActionDispatcher>();
        services.AddScoped<FlowOS.Core.Common.Interfaces.IDeadLetterService, DeadLetterService>();
        services.AddScoped<FlowOS.Core.Common.Interfaces.IWorkflowActionHistoryService, WorkflowActionHistoryService>();
        services.AddScoped<FlowOS.Core.Common.Interfaces.IWorkflowTimeTravelService, WorkflowTimeTravelService>();
        services.AddScoped<FlowOS.Core.Common.Interfaces.IIdempotencyService, IdempotencyService>();
        services.AddScoped<FlowOS.Core.Common.Interfaces.IRetryPolicyService, RetryPolicyService>();
        services.AddScoped<FlowOS.Core.Common.Interfaces.ICompensationPlannerService, CompensationPlannerService>();
        services.AddScoped<FlowOS.Core.Common.Interfaces.ICapabilityRegistryService, CapabilityRegistryService>();
        services.AddScoped<FlowOS.Core.Common.Interfaces.IPluginBindingRegistryService, PluginBindingRegistryService>();
        services.AddScoped<IActivityAuthorizationService, FlowOS.Application.Services.ActivityAuthorizationService>();
        services.AddScoped<IWorkflowContextBindingValidator, FlowOS.Application.Services.WorkflowContextBindingValidator>();
        services.AddScoped<IWorkflowContextMaterializer, FlowOS.Application.Services.WorkflowContextMaterializer>();
        services.AddScoped<IBusinessRoleResolver, FlowOS.Application.Services.BusinessRoleResolver>();
        services.AddScoped<IWorkflowExecutionContextService, FlowOS.Application.Services.WorkflowExecutionContextService>();
        services.AddScoped<IWorkflowContextSimulationService, FlowOS.Application.Services.WorkflowContextSimulationService>();
        services.AddScoped<WorkflowDefinitionLineageBackfillService>();
        services.AddScoped<IDecisionPacketBuilder, FlowOS.Application.Services.DecisionPacketBuilder>();
        services.AddSingleton(provider =>
        {
            var options = new LlmTransportOptions();
            provider.GetService<IConfiguration>()
                ?.GetSection(LlmTransportOptions.ConfigurationSection)
                .Bind(options);
            return options;
        });
        services.AddHttpClient(
                LlmHttpTransport.HttpClientName,
                (provider, client) =>
                {
                    client.Timeout = provider
                        .GetRequiredService<LlmTransportOptions>()
                        .Timeout;
                })
            .RedactLoggedHeaders(
            [
                "Authorization",
                "api-key",
                "x-api-key",
                "x-goog-api-key"
            ]);
        services.AddSingleton<ILlmTransport, LlmHttpTransport>();
        services.AddScoped<IAgentTaskRunner, FlowOS.Application.Services.AgentTaskRunner>();
        services.AddScoped<IAgentTaskCoordinator, FlowOS.Application.Services.AgentTaskCoordinator>();
        services.AddScoped<IWorkflowAgentFactory>(provider =>
            new FlowOS.Application.Services.WorkflowAgentFactory(
                provider.GetRequiredService<FlowOS.Core.Common.Interfaces.IPluginBindingRegistryService>(),
                hosted: provider.GetRequiredService<IFlowOsHostedLlmRuntime>(),
                transport: provider.GetRequiredService<ILlmTransport>()));
        services.AddScoped<IFlowOsHostedLlmRuntime, FlowOsHostedLlmRuntime>();
        services.AddScoped<IAgentTaskQueue, AgentTaskQueue>();
        services.AddScoped<IDistributedLeaseService, DistributedLeaseService>();
        services.AddScoped<IHostedLlmUsageStore, HostedLlmUsageStore>();
        services.AddScoped<AgentExecutionStore>();
        services.AddScoped<IAgentExecutionRecorder>(provider => provider.GetRequiredService<AgentExecutionStore>());
        services.AddScoped<IAgentExecutionHistoryStore>(provider => provider.GetRequiredService<AgentExecutionStore>());
        services.AddScoped<IAgentObservabilityQueryService, FlowOS.Application.Services.AgentObservabilityQueryService>();
        services.AddScoped(provider =>
        {
            var options = new AgentPersistenceRetentionOptions();
            provider.GetService<IConfiguration>()
                ?.GetSection(AgentPersistenceRetentionOptions.ConfigurationSection)
                .Bind(options);
            return options;
        });
        services.AddScoped<IAgentPersistenceCleanupService, AgentPersistenceCleanupService>();
        services.AddSingleton<FlowOS.Core.Common.Interfaces.IWebhookSignatureService, FlowOS.Core.Common.Services.WebhookSignatureService>();
        services.AddSingleton<FlowOS.Security.Interfaces.IPasswordHasher, FlowOS.Infrastructure.Services.Security.Pbkdf2PasswordHasher>();
        services.AddSingleton<FlowOS.Security.Interfaces.IJwtTokenService, FlowOS.Infrastructure.Services.Security.JwtTokenService>();
        services.AddScoped<FlowOS.Application.Common.Interfaces.ITenantAuthService, FlowOS.Infrastructure.Services.Security.TenantAuthService>();
        services.AddScoped<FlowOS.Application.Common.Interfaces.ITenantEntitlementService, FlowOS.Infrastructure.Services.TenantEntitlementService>();
        return services;
    }
}
