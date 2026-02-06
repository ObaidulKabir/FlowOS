using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using FlowOS.Infrastructure.Persistence;
using System.Collections.Generic;
using System;
using System.Linq;

namespace FlowOS.E2E.Tests;

public class FlowOSWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"FlowOS_Db_{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "UseInMemoryDatabase", "true" },
                { "ConnectionStrings:DefaultConnection", "ignored-in-memory" }
            });
        });

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<FlowOSDbContext>));

            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<FlowOSDbContext>((sp, options) =>
            {
                options.UseInMemoryDatabase(_dbName);
            });
        });
    }
}
