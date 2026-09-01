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
        return services;
    }
}
