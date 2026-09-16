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

        Assert.Contains("MCP runtime included", landing);
        Assert.Contains("No per-call or usage fees", landing);
        Assert.Contains("design-only until activated", landing);
        Assert.Contains("MCP is included", docs);
        Assert.Contains("no per-call or per-transition overage", docs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No usage fees", docs);
        Assert.Contains("MCP is included in a paid FlowOS tenant subscription", chapter13);
        Assert.DoesNotContain("Pre-Payment Gateway", chapter13);
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
