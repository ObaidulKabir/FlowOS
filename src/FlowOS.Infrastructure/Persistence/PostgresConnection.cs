using System;

namespace FlowOS.Infrastructure.Persistence;

public static class PostgresConnection
{
    public static bool HasUsableHost(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return false;

        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0)
                continue;

            var key = part[..separator].Trim();
            var value = part[(separator + 1)..].Trim();
            if (key.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("Server", StringComparison.OrdinalIgnoreCase))
            {
                return value.Length > 0;
            }
        }

        return false;
    }
}
