using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace FlowOS.Infrastructure.Persistence;

public class FlowOSDbContextFactory : IDesignTimeDbContextFactory<FlowOSDbContext>
{
    public FlowOSDbContext CreateDbContext(string[] args)
    {
        var basePath = Directory.GetCurrentDirectory();
        var apiPath = Path.Combine(basePath, "src", "FlowOS.Api");
        if (!Directory.Exists(apiPath))
            apiPath = basePath;

        var configuration = new ConfigurationBuilder()
            .SetBasePath(apiPath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
        {
            connectionString = "Host=localhost;Database=flowos_dev;Username=postgres;Password=postgres";
        }

        var optionsBuilder = new DbContextOptionsBuilder<FlowOSDbContext>();
        optionsBuilder.UseNpgsql(connectionString, b => b.MigrationsAssembly(typeof(FlowOSDbContext).Assembly.FullName));

        return new FlowOSDbContext(optionsBuilder.Options);
    }
}
