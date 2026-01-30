using System;
using System.Reflection;
using System.Threading.Tasks;
using FlowOS.Domain.Entities;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting; // Added for IHostEnvironment
using Microsoft.Extensions.Logging;

using FlowOS.Domain.Enums;

namespace FlowOS.API.Services;

public static class DataSeeder
{
    public static readonly Guid DefaultTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static async Task SeedAsync(FlowOSDbContext context, IServiceProvider serviceProvider, IHostEnvironment env)
    {
        // 1. Ensure Tenant
        if (!await context.Tenants.AnyAsync())
        {
            var tenant = new Tenant("Default Tenant");
            SetPrivateProperty(tenant, "TenantId", DefaultTenantId);
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();
        }

        // 2. Load Configuration (Dev Only)
        if (env.IsDevelopment())
        {
            // Locate config folder relative to execution
            var configRoot = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "flowos-config"); 
            // Adjust path if running from bin/Debug... for local dev, usually relative to project root or solution
            if (!Directory.Exists(configRoot))
            {
                 // Try standard dev path
                 configRoot = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "flowos-config");
            }

            if (Directory.Exists(configRoot))
            {
                var logger = serviceProvider.GetRequiredService<ILogger<ConfigurationLoader>>();
                var loader = new ConfigurationLoader(context, logger, configRoot);
                await loader.LoadAllAsync(DefaultTenantId);
            }
        }
    }

    private static void SetPrivateProperty(object obj, string propName, object value)
    {
        var prop = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (prop != null && prop.CanWrite)
        {
            prop.SetValue(obj, value);
        }
    }
}
