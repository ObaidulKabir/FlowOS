using System.IO;
using FlowOS.Core.Common.Models;
using FlowOS.Domain;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.ValueObjects;

namespace FlowOS.UnitTests.Domain;

public class FlowOsReleaseTests
{
    [Fact]
    public void CurrentRelease_IsTheDesignedAppContract()
    {
        Assert.Equal("1.2.2", FlowOsRelease.Version);
        Assert.Equal("1.1.0", FlowOsRelease.PreStampVersion);
        Assert.Equal(FlowOsCompatibility.Current, FlowOsRelease.Assess(FlowOsRelease.Version));
        Assert.Equal(FlowOsCompatibility.OlderCompatible, FlowOsRelease.Assess(FlowOsRelease.PreStampVersion));
        Assert.Equal(FlowOsCompatibility.NewerThanHost, FlowOsRelease.Assess("1.2.3"));
        Assert.Equal(FlowOsCompatibility.IncompatibleMajor, FlowOsRelease.Assess("2.0.0"));
        Assert.Equal(FlowOsCompatibility.Unreadable, FlowOsRelease.Assess("latest"));
    }

    [Fact]
    public void DesignedAppPieces_RecordTheHostRelease()
    {
        var workflowClass = new WorkflowClass(Guid.NewGuid(), "Expense", "1.0.0", new WorkflowClassBlueprint());
        workflowClass.UpdateDraft("Expense", "1.0.1", new WorkflowClassBlueprint(), "clarify a step");

        var revision = new WorkflowContextBindingRevision(
            Guid.NewGuid(),
            1,
            workflowClass.Id,
            workflowClass.Version,
            new WorkflowContextBindingDefinition());
        revision.UpdateDraft(workflowClass.Id, workflowClass.Version, new WorkflowContextBindingDefinition());

        var binding = new PluginBindingRecord(Guid.NewGuid(), "prompt", "quote-approval", "markdown");
        binding.Update("markdown", true, "{\"instructions\":\"approve\"}");

        Assert.Equal(FlowOsRelease.Version, workflowClass.FlowOsVersion);
        Assert.Equal(FlowOsRelease.Version, revision.FlowOsVersion);
        Assert.Equal(FlowOsRelease.Version, binding.FlowOsVersion);
        Assert.NotEqual(workflowClass.Version, workflowClass.FlowOsVersion);
    }

    [Fact]
    public void DocsAndManifests_NameTheCurrentRelease()
    {
        var root = FindRepoRoot();
        var chapter = File.ReadAllText(Path.Combine(root, "docs", "22-designed-app-flowos-version.md"));
        var dashboardManifest = File.ReadAllText(Path.Combine(root, "apps", "dashboard", "public", ".well-known", "mcp.json"));
        var apiManifest = File.ReadAllText(Path.Combine(root, "src", "FlowOS.Api", "wwwroot", ".well-known", "mcp.json"));

        Assert.Contains(FlowOsRelease.Version, chapter);
        Assert.Contains("Bump the **patch**", chapter);
        Assert.Contains($"\"flowOsVersion\": \"{FlowOsRelease.Version}\"", dashboardManifest);
        Assert.Contains($"\"flowOsVersion\": \"{FlowOsRelease.Version}\"", apiManifest);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FlowOS.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate FlowOS.sln from the test output directory.");
    }
}
