using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using System.IO;

namespace FlowOS.UnitTests.Integration;

public class CustomWebApplicationFactory<TProgram> : WebApplicationFactory<TProgram> where TProgram : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        var projectDir = GetProjectPath("src", "FlowOS.Api");
        builder.UseContentRoot(projectDir);
        base.ConfigureWebHost(builder);
    }

    private static string GetProjectPath(string projectRelativePath, string projectName)
    {
        var applicationBasePath = System.AppContext.BaseDirectory;
        var directoryInfo = new DirectoryInfo(applicationBasePath);

        // Navigate up until we find the solution file or src folder
        while (directoryInfo.Parent != null)
        {
            directoryInfo = directoryInfo.Parent;
            var projectDirectoryInfo = new DirectoryInfo(Path.Combine(directoryInfo.FullName, projectRelativePath, projectName));
            if (projectDirectoryInfo.Exists)
            {
                return projectDirectoryInfo.FullName;
            }
        }

        throw new DirectoryNotFoundException($"Project root '{projectName}' not found.");
    }
}
