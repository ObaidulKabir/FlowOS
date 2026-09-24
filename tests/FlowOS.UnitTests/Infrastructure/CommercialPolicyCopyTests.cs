using System;
using System.IO;
using Xunit;

namespace FlowOS.UnitTests.Infrastructure;

public class CommercialPolicyCopyTests
{
    [Fact]
    public void LandingAndDocs_StateMcpIsIncluded_WithNoPerCallFee()
    {
        var root = FindRepoRoot();
        var landing = File.ReadAllText(Path.Combine(root, "apps", "dashboard", "src", "components", "LandingPage.tsx"));
        var docs = File.ReadAllText(Path.Combine(root, "docs", "18-commercial-and-mcp-entitlements.md"));
        var chapter13 = File.ReadAllText(Path.Combine(root, "docs", "13-mcp-and-ai-agent-integration.md"));

        Assert.Contains("Try, build, then grow with usage", landing);
        Assert.Contains("will not charge per retry, per simulation, or per transition", landing);
        Assert.Contains("Register is Free (design-time)", landing);
        Assert.Contains("MCP is included", docs);
        Assert.Contains("will not charge per retry, simulation, replay, or compensation", docs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Free → Starter → Builder → Team → Growth → Scale → Enterprise", docs);
        Assert.Contains("MCP is included on every package", chapter13);
        Assert.DoesNotContain("Pre-Payment Gateway", chapter13);
        Assert.DoesNotContain("Managed Cloud $299", docs);
        Assert.DoesNotContain("Managed Cloud $299", chapter13);
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
