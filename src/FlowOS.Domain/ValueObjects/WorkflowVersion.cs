using System;

namespace FlowOS.Domain.ValueObjects;

/// <summary>
/// Parses WorkflowClass SemVer strings (e.g. "1.2.0", "v1.0.0") for runtime mapping.
/// </summary>
public readonly struct WorkflowVersion : IEquatable<WorkflowVersion>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public string Original { get; }

    public WorkflowVersion(int major, int minor = 0, int patch = 0, string? original = null)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Original = original ?? $"{major}.{minor}.{patch}";
    }

    public static WorkflowVersion Parse(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return new WorkflowVersion(1, 0, 0, "1.0.0");

        var versionStr = version.Trim();
        if (versionStr.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            versionStr = versionStr[1..];

        if (System.Version.TryParse(versionStr, out var v))
        {
            var major = Math.Max(v.Major, 0);
            var minor = Math.Max(v.Minor, 0);
            var patch = v.Build < 0 ? 0 : v.Build;
            return new WorkflowVersion(major, minor, patch, version);
        }

        var majorPart = versionStr.Split(new[] { '.', '-', '+' })[0];
        if (int.TryParse(majorPart, out var majorOnly))
            return new WorkflowVersion(majorOnly, 0, 0, version);

        return new WorkflowVersion(1, 0, 0, version);
    }

    /// <summary>Major version used when compiling a WorkflowClass into a runtime WorkflowDefinition.</summary>
    public int RuntimeVersion => Major <= 0 ? 1 : Major;

    /// <summary>Bump minor: 1.0.0 → 1.1.0</summary>
    public WorkflowVersion BumpMinor()
        => new(Major, Minor + 1, Patch, null);

    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    public bool Equals(WorkflowVersion other)
        => Major == other.Major && Minor == other.Minor && Patch == other.Patch;

    public override bool Equals(object? obj) => obj is WorkflowVersion other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch);
}
