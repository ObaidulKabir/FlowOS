using System;
using System.IO;

namespace FlowOS.Infrastructure.Services;

/// <summary>
/// Ensures that file operations occur within a user-specified project root
/// and not accidentally in the execution (bin) directory.
/// </summary>
public static class WorkingDirectoryValidator
{
    /// <summary>
    /// Validates the provided path as a legitimate project working directory.
    /// Throws if the path is invalid, does not exist, or appears to be a build artifact directory.
    /// </summary>
    /// <param name="path">The absolute path to the project root.</param>
    public static void Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Working directory path cannot be null or empty.", nameof(path));

        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"The specified working directory does not exist: {path}");

        // Heuristic: Prevent usage of bin/Debug or obj folders as project root
        // This enforces the rule that "Working Directory" != "Execution Directory"
        var normalized = Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
        
        if (normalized.EndsWith("/bin/Debug") || 
            normalized.EndsWith("/bin/Release") || 
            normalized.Contains("/bin/Debug/") || 
            normalized.Contains("/bin/Release/"))
        {
            throw new InvalidOperationException(
                $"Invalid Working Directory: '{path}'. " +
                "The working directory must be the project root (e.g., C:\\Projects\\FlowOS), " +
                "not the binary execution directory.");
        }
    }
}
