using System;
using System.IO;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace FlowOS.Infrastructure;

/// <summary>
/// Registers ASP.NET Core Data Protection the same way in every FlowOS host (API, MCP HTTP, MCP stdio,
/// MCP agent worker).
///
/// Data Protection encrypts tenant secrets at rest, such as BYO LLM API keys on agent provider bindings
/// and webhook secrets. A value protected by one host must be readable by every other host, and must
/// stay readable after a redeploy. That needs two things:
/// <list type="bullet">
/// <item>One application name, so all hosts derive the same purpose keys.</item>
/// <item>One persisted key ring, so keys survive container recreation and are shared between hosts.
/// Set <c>FLOWOS_DATA_PROTECTION_KEYS_PATH</c> to a directory on a volume mounted into every host.</item>
/// </list>
/// Without the path, keys live in the container's default location (or in memory) and are lost when
/// the container is recreated; Data Protection logs a warning at first use in that case. That is fine
/// for tests and local Development, not for Production.
/// </summary>
public static class FlowOsDataProtectionExtensions
{
    public const string ApplicationName = "FlowOS";
    public const string KeysPathVariable = "FLOWOS_DATA_PROTECTION_KEYS_PATH";

    public static IServiceCollection AddFlowOsDataProtection(this IServiceCollection services) =>
        services.AddFlowOsDataProtection(Environment.GetEnvironmentVariable(KeysPathVariable));

    public static IServiceCollection AddFlowOsDataProtection(this IServiceCollection services, string? keysPath)
    {
        var builder = services.AddDataProtection().SetApplicationName(ApplicationName);
        if (!string.IsNullOrWhiteSpace(keysPath))
            builder.PersistKeysToFileSystem(new DirectoryInfo(keysPath.Trim()));

        return services;
    }
}
