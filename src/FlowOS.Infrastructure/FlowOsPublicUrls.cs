using Microsoft.Extensions.Configuration;

namespace FlowOS.Infrastructure;

public static class FlowOsPublicUrls
{
    public const string StagingOrigin = "https://flowos.prospectbdltd.com";
    public const string ProductionOrigin = "https://flowosbd.com";

    public static string ResolveOrigin(
        IConfiguration configuration,
        string? requestScheme = null,
        string? requestHost = null,
        string? forwardedProto = null,
        string? forwardedHost = null)
    {
        var configured = configuration["FLOWOS_PUBLIC_ORIGIN"];
        if (!string.IsNullOrWhiteSpace(configured))
            return NormalizeOrigin(configured);

        var domain = configuration["FLOWOS_DOMAIN"];
        if (!string.IsNullOrWhiteSpace(domain))
        {
            var host = domain.Trim().TrimEnd('/');
            if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return NormalizeOrigin(host);

            return $"https://{host}";
        }

        var scheme = FirstHeaderValue(forwardedProto) ?? requestScheme ?? "https";
        var hostHeader = FirstHeaderValue(forwardedHost) ?? requestHost;
        if (string.IsNullOrWhiteSpace(hostHeader))
            return StagingOrigin;

        return $"{scheme}://{hostHeader.Trim().TrimEnd('/')}";
    }

    public static string McpEndpoint(string origin)
    {
        var baseOrigin = NormalizeOrigin(origin);
        if (string.IsNullOrWhiteSpace(baseOrigin))
            return "/mcp";

        return IsProductionHost(baseOrigin)
            ? $"{baseOrigin}/mcp/"
            : $"{baseOrigin}/mcp";
    }

    public static bool IsMcpPath(string? path) =>
        string.Equals(path, "/mcp", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(path, "/mcp/", StringComparison.OrdinalIgnoreCase);

    public static bool IsProductionHost(string originOrUrl)
    {
        if (Uri.TryCreate(originOrUrl, UriKind.Absolute, out var uri))
        {
            return uri.Host.Equals("flowosbd.com", StringComparison.OrdinalIgnoreCase) ||
                   uri.Host.EndsWith(".flowosbd.com", StringComparison.OrdinalIgnoreCase);
        }

        return originOrUrl.Contains("flowosbd.com", StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeOrigin(string? origin) =>
        (origin ?? string.Empty).Trim().TrimEnd('/');

    private static string? FirstHeaderValue(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
            return null;

        return header.Split(',')[0].Trim();
    }
}
