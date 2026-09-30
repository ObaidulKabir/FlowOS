namespace FlowOS.Domain;

/// <summary>
/// Compatibility contract for a DesignedApp: a WorkflowClass blueprint, its Business Context, and its AI Context.
/// This is not the blueprint's own version. Major and minor are changed by hand. The build is
/// <c>FLOWOS_BUILD</c>, which deploy sets to the git commit count.
/// </summary>
public static class FlowOsRelease
{
    /// <summary>Compatibility line. Change these by hand. The build number moves on its own.</summary>
    public const int Major = 1;

    /// <summary>Compatibility line. Change this by hand when older DesignedApps on this major still run.</summary>
    public const int Minor = 2;

    /// <summary>Build used when <c>FLOWOS_BUILD</c> is not set. Deploy sets that variable to the git commit count.</summary>
    public const int RecordedBuild = 2;

    /// <summary>Last MCP server release before DesignedApps recorded a FlowOS dependency.</summary>
    public const string PreStampVersion = "1.1.0";

    /// <summary>Platform and workflow versions are always three integer parts: major, minor, and build.</summary>
    public const string Scheme = "Major.Minor.Build";

    /// <summary>Major.Minor plus the deployed build. The build is <c>FLOWOS_BUILD</c> when that is a positive integer, otherwise <see cref="RecordedBuild"/>.</summary>
    public static string Version => Format(Build);

    public static int Build
    {
        get
        {
            var raw = Environment.GetEnvironmentVariable("FLOWOS_BUILD");
            if (int.TryParse(raw, out var build) && build > 0)
                return build;
            return RecordedBuild;
        }
    }

    public static string Format(int build) => $"{Major}.{Minor}.{build}";

    public static string Display => $"FlowOS {Version}";

    public static FlowOsCompatibility Assess(string? artifactVersion, string? hostVersion = null)
    {
        if (!TryParse(artifactVersion, out var artifact))
            return FlowOsCompatibility.Unreadable;
        if (!TryParse(string.IsNullOrWhiteSpace(hostVersion) ? Version : hostVersion, out var host))
            return FlowOsCompatibility.Unreadable;
        if (artifact.Major != host.Major)
            return FlowOsCompatibility.IncompatibleMajor;
        var compare = artifact.CompareTo(host);
        if (compare > 0)
            return FlowOsCompatibility.NewerThanHost;
        if (compare < 0)
            return FlowOsCompatibility.OlderCompatible;
        return FlowOsCompatibility.Current;
    }

    public static bool TryParse(string? value, out FlowOsVersionNumber version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var parts = value.Trim().Split('.');
        if (parts.Length != 3)
            return false;
        if (!int.TryParse(parts[0], out var major) || major < 0)
            return false;
        if (!int.TryParse(parts[1], out var minor) || minor < 0)
            return false;
        if (!int.TryParse(parts[2], out var build) || build < 0)
            return false;

        version = new FlowOsVersionNumber(major, minor, build);
        return true;
    }
}

public enum FlowOsCompatibility
{
    Current,
    OlderCompatible,
    NewerThanHost,
    IncompatibleMajor,
    Unreadable
}

public readonly record struct FlowOsVersionNumber(int Major, int Minor, int Build) : IComparable<FlowOsVersionNumber>
{
    public int CompareTo(FlowOsVersionNumber other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0) return major;
        var minor = Minor.CompareTo(other.Minor);
        if (minor != 0) return minor;
        return Build.CompareTo(other.Build);
    }

    public override string ToString() => $"{Major}.{Minor}.{Build}";
}
