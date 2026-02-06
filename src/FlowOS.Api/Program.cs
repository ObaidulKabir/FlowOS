using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using MediatR;
using FlowOS.Core.Interfaces;
// using FlowOS.Application.Common.Interfaces; // Might still be needed for other things?
using FlowOS.Application.Common.Interfaces; // Kept if needed
using FlowOS.Core.Interfaces;
using FlowOS.API.Services;
using FlowOS.Security.Policies;
using FlowOS.Application.Behaviors;
using FlowOS.API.Filters; // Case sensitive? FlowOS.API or FlowOS.Api?
// The folder is src/FlowOS.Api/Filters
// The namespace in ApiExceptionFilterAttribute.cs is usually FlowOS.API.Filters or FlowOS.Api.Filters.
// Let's check.
using FlowOS.Security.Interfaces;
using FlowOS.Infrastructure.Services;
using FlowOS.Api.Middleware;
using FlowOS.Notifications.Application;
using FlowOS.Notifications.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication; // Add this

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers(options => 
    options.Filters.Add<ApiExceptionFilterAttribute>());

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUserService>();

// Add Authentication Scheme to prevent 500 errors on Challenge/Forbid
builder.Services.AddAuthentication("Mock")
    .AddScheme<AuthenticationSchemeOptions, MockAuthenticationHandler>("Mock", null);

// Add Policy Services
builder.Services.AddScoped<IPolicyProvider, EfCorePolicyProvider>();
builder.Services.AddScoped<IPolicyEvaluator, DefaultPolicyEvaluator>();

// Register Notification Services
builder.Services.AddSingleton<NotificationStreamService>();
builder.Services.AddScoped<EventPublishingInterceptor>();
// Register Repository/Service for Notification Abstractions
builder.Services.AddScoped<NotificationRepository>();
builder.Services.AddScoped<INotificationRepository>(sp => sp.GetRequiredService<NotificationRepository>());
builder.Services.AddScoped<INotificationQueryService>(sp => sp.GetRequiredService<NotificationRepository>());

// Add DbContext
    builder.Services.AddDbContext<FlowOSDbContext>((sp, options) =>
    {
        var interceptor = sp.GetRequiredService<EventPublishingInterceptor>(); 
        
        // Use InMemory for testing if DB is not available (Simulated Logic)
        // In real dev, we might check a flag or environment variable.
        bool useInMemory = builder.Configuration.GetValue<bool>("UseInMemoryDatabase");
        
        if (useInMemory)
        {
            options.UseInMemoryDatabase("FlowOS_Db")
                   .AddInterceptors(interceptor);
        }
        else
        {
            var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
            if (!string.IsNullOrEmpty(connectionString))
            {
                options.UseNpgsql(connectionString)
                       .AddInterceptors(interceptor);
            }
            else
            {
                throw new InvalidOperationException("Database connection string 'DefaultConnection' is missing.");
            }
        }
    });

// Infrastructure Services
builder.Services.AddScoped<IEventRegistry, EventRegistry>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ICapabilityService, CapabilityService>();

// Governance Domain Services
builder.Services.AddScoped<FlowOS.Domain.Services.WorkflowClassValidator>();
builder.Services.AddScoped<FlowOS.Domain.Services.WorkflowClassManager>();

// Add MediatR
builder.Services.AddMediatR(cfg => {
    cfg.RegisterServicesFromAssemblies(
        typeof(FlowOS.Application.Commands.StartWorkflowCommand).Assembly,
        typeof(FlowOS.Notifications.Application.NotificationProjector).Assembly // Add Notifications Assembly
    );
    cfg.AddOpenBehavior(typeof(PolicyEnforcementBehavior<,>));
});

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowDashboard",
        policy =>
        {
            policy.WithOrigins("http://localhost:3000", "http://localhost:5173") // Allow Vite Dashboard
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseCors("AllowDashboard"); // Enable CORS for Dev
}

// FORCE MOCK AUTH FOR TESTING
app.UseMiddleware<MockAuthMiddleware>();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Seed Data
    using (var scope = app.Services.CreateScope())
    {
        var context = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
        
        // Only migrate if Relational
        if (context.Database.IsRelational())
        {
            context.Database.Migrate();
        }
        else
        {
            context.Database.EnsureCreated(); // For InMemory
        }
        
        await DataSeeder.SeedAsync(context, scope.ServiceProvider, app.Environment);
    }

app.Run();

public partial class Program { }

public partial class Program { }
