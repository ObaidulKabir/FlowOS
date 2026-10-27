using System;
using System.Linq;

namespace FlowOS.Core.Security;

/// <summary>
/// Rules for the secret that signs tenant login tokens (JWT). The repository is public, so a value that
/// was ever committed must never sign production tokens: anyone could forge a login for any tenant.
/// </summary>
public static class JwtSecretRules
{
    public const string ConfigKey = "FlowOS:Auth:JwtSecret";
    public const string EnvironmentVariable = "FlowOS__Auth__JwtSecret";
    public const int MinimumLength = 32;

    /// <summary>Used only when running in Development without a configured secret.</summary>
    public const string DevelopmentOnlySecret = "flowos-development-only-jwt-secret-never-use-in-production";

    // Values that appear, or appeared, in the public repository.
    private static readonly string[] PublicValues =
    {
        "FlowOS_Super_Secret_Key_For_Tenant_Auth_2026_Minimum_32_Chars!",
        DevelopmentOnlySecret
    };

    public static bool IsDevelopment(string? environmentName) =>
        string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the secret to use. Outside Development a missing, short or public value throws, so the host
    /// refuses to start instead of signing tokens anyone can forge.
    /// </summary>
    public static string Resolve(string? configured, string? environmentName)
    {
        var development = IsDevelopment(environmentName);
        if (string.IsNullOrWhiteSpace(configured))
        {
            if (development) return DevelopmentOnlySecret;
            throw new InvalidOperationException(
                $"{ConfigKey} is not set. Set {EnvironmentVariable} to a random value of at least {MinimumLength} characters.");
        }

        if (!development && configured.Length < MinimumLength)
            throw new InvalidOperationException($"{ConfigKey} must be at least {MinimumLength} characters.");
        if (!development && PublicValues.Contains(configured, StringComparer.Ordinal))
            throw new InvalidOperationException(
                $"{ConfigKey} is set to a value published in the FlowOS repository. Replace it with a new random secret.");

        return configured;
    }
}
