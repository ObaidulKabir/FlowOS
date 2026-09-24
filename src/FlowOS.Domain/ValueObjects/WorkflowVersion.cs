using System;
using FlowOS.Domain.Enums;

namespace FlowOS.Domain.ValueObjects;

/// <summary>
/// Parses WorkflowClass SemVer strings (e.g. "1.2.0", "v1.0.0") for runtime mapping.
/// </summary>
public readonly struct WorkflowVersion : IEquatable<WorkflowVersion>, IComparable<WorkflowVersion>
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

        throw new FormatException($"Cannot parse '{version}' as a valid workflow version. Expected format: Major.Minor.Patch (e.g. '1.0.0').");
    }

    /// <summary>Attempts to parse without throwing. Returns false on failure.</summary>
    public static bool TryParse(string? version, out WorkflowVersion result)
    {
        try
        {
            result = Parse(version);
            return true;
        }
        catch (FormatException)
        {
            result = default;
            return false;
        }
    }

    /// <summary>Major version used when compiling a WorkflowClass into a runtime WorkflowDefinition.</summary>
    public int RuntimeVersion => Major <= 0 ? 1 : Major;

    /// <summary>Bump major: 1.2.3 → 2.0.0 (breaking change)</summary>
    public WorkflowVersion BumpMajor()
        => new(Major + 1, 0, 0, null);

    /// <summary>Bump minor: 1.0.0 → 1.1.0 (backwards-compatible change)</summary>
    public WorkflowVersion BumpMinor()
        => new(Major, Minor + 1, 0, null);

    /// <summary>Bump patch: 1.0.0 → 1.0.1 (hotfix)</summary>
    public WorkflowVersion BumpPatch()
        => new(Major, Minor, Patch + 1, null);

    /// <summary>Bumps the version based on the specified bump type.</summary>
    public WorkflowVersion Bump(VersionBumpType bumpType) => bumpType switch
    {
        VersionBumpType.Major => BumpMajor(),
        VersionBumpType.Minor => BumpMinor(),
        VersionBumpType.Patch => BumpPatch(),
        _ => throw new ArgumentOutOfRangeException(nameof(bumpType))
    };

    public int CompareTo(WorkflowVersion other)
    {
        var majorCmp = Major.CompareTo(other.Major);
        if (majorCmp != 0) return majorCmp;
        var minorCmp = Minor.CompareTo(other.Minor);
        if (minorCmp != 0) return minorCmp;
        return Patch.CompareTo(other.Patch);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    public bool Equals(WorkflowVersion other)
        => Major == other.Major && Minor == other.Minor && Patch == other.Patch;

    public override bool Equals(object? obj) => obj is WorkflowVersion other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch);

    public static bool operator ==(WorkflowVersion left, WorkflowVersion right) => left.Equals(right);
    public static bool operator !=(WorkflowVersion left, WorkflowVersion right) => !left.Equals(right);
    public static bool operator <(WorkflowVersion left, WorkflowVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(WorkflowVersion left, WorkflowVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(WorkflowVersion left, WorkflowVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(WorkflowVersion left, WorkflowVersion right) => left.CompareTo(right) >= 0;
}
