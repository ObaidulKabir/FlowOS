using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;

namespace FlowOS.Infrastructure.Persistence;

public static class FlowOsDatabase
{
    public static bool IsLiveEnvironment(string? environmentName) =>
        string.IsNullOrWhiteSpace(environmentName) ||
        environmentName.Equals("Production", StringComparison.OrdinalIgnoreCase) ||
        environmentName.Equals("Staging", StringComparison.OrdinalIgnoreCase);

    public static bool AllowsInMemory(string? environmentName, bool useInMemoryFlag) =>
        useInMemoryFlag && !IsLiveEnvironment(environmentName);

    public static void Configure(
        DbContextOptionsBuilder options,
        string? environmentName,
        IConfiguration configuration,
        string inMemoryDatabaseName = "FlowOS_Db",
        params IInterceptor[] interceptors)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? configuration["ConnectionStrings:DefaultConnection"];
        var useInMemoryFlag = configuration.GetValue("UseInMemoryDatabase", false);
        var hasPostgres = PostgresConnection.HasUsableHost(connectionString);

        if (hasPostgres)
        {
            options.UseNpgsql(connectionString);
            if (interceptors.Length > 0)
                options.AddInterceptors(interceptors);
            return;
        }

        if (AllowsInMemory(environmentName, useInMemoryFlag))
        {
            options.UseInMemoryDatabase(inMemoryDatabaseName);
            if (interceptors.Length > 0)
                options.AddInterceptors(interceptors);
            return;
        }

        throw new InvalidOperationException(
            "Tenant API keys must be stored in PostgreSQL. Set ConnectionStrings:DefaultConnection with a non-empty Host. " +
            "In-memory storage is only allowed in Development when UseInMemoryDatabase=true.");
    }

    public static bool IsNpgsql(DbContextOptionsBuilder options) =>
        options.Options.Extensions.Any(extension =>
            extension.GetType().Name.Contains("Npgsql", StringComparison.OrdinalIgnoreCase));

    public static bool IsInMemory(DbContextOptionsBuilder options) =>
        options.Options.Extensions.Any(extension =>
            extension.GetType().Name.Contains("InMemory", StringComparison.OrdinalIgnoreCase));
}
