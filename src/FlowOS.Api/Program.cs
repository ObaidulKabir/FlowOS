using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using MediatR;
using System.Reflection;
using FlowOS.Application.Common.Interfaces;
using FlowOS.API.Services;
using FlowOS.Security.Policies;
using FlowOS.Application.Behaviors;

using FlowOS.API.Filters;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers(options => 
    options.Filters.Add<ApiExceptionFilterAttribute>());

// Add HttpContextAccessor and CurrentUser
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUserService>();

// Add Policy Services
builder.Services.AddScoped<IPolicyProvider, AllowAllPolicyProvider>();
builder.Services.AddScoped<IPolicyEvaluator, DefaultPolicyEvaluator>();

// Add DbContext
builder.Services.AddDbContext<FlowOSDbContext>(options =>
{
    options.UseInMemoryDatabase("FlowOS_Db");
    options.EnableSensitiveDataLogging();
});

// Add MediatR
builder.Services.AddMediatR(cfg => {
    cfg.RegisterServicesFromAssemblies(
        typeof(FlowOS.Application.Commands.StartWorkflowCommand).Assembly,
        typeof(FlowOS.Application.Handlers.WorkflowCommandHandlers).Assembly
    );
    // Register Pipeline Behaviors
    cfg.AddOpenBehavior(typeof(PolicyEnforcementBehavior<,>));
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapControllers();

app.Run();

// Make Program public for integration tests
public partial class Program { }
