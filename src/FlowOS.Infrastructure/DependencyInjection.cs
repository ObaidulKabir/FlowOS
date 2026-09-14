using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Infrastructure.Persistence.Repositories;
using FlowOS.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FlowOS.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the Unit of Work and repository abstractions used by the Application layer.
    /// </summary>
    public static IServiceCollection AddFlowOSPersistence(this IServiceCollection services)
    {
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
        services.AddSingleton<FlowOS.Core.Common.Interfaces.IWebhookSignatureService, FlowOS.Core.Common.Services.WebhookSignatureService>();
        return services;
    }
}
