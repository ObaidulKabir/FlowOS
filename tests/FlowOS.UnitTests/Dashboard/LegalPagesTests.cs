using System.IO;
using System.Text.Json;

namespace FlowOS.UnitTests.Dashboard;

public class LegalPagesTests
{
    [Fact]
    public void PublicLegalPages_NameTheSellerAndEachOther()
    {
        var root = FindRepoRoot();
        var entityPath = Path.Combine(root, "apps", "dashboard", "src", "legalEntity.json");
        using var entity = JsonDocument.Parse(File.ReadAllText(entityPath));
        var rootElement = entity.RootElement;
        var operatorName = rootElement.GetProperty("operatorName").GetString();
        var email = rootElement.GetProperty("supportEmail").GetString();
        var site = rootElement.GetProperty("siteUrl").GetString();

        Assert.False(string.IsNullOrWhiteSpace(operatorName));
        Assert.False(string.IsNullOrWhiteSpace(email));
        Assert.StartsWith("https://", site);

        foreach (var file in new[] { "terms.html", "privacy.html", "refunds.html", "acceptable-use.html", "contact.html" })
        {
            var html = File.ReadAllText(Path.Combine(root, "apps", "dashboard", "public", file));
            Assert.Contains(operatorName!, html);
            Assert.Contains(email!, html);
            Assert.Contains("href=\"/terms\"", html);
            Assert.Contains("href=\"/privacy\"", html);
            Assert.Contains("href=\"/refunds\"", html);
            Assert.Contains("href=\"/acceptable-use\"", html);
            Assert.Contains("href=\"/contact\"", html);
        }

        var landing = File.ReadAllText(Path.Combine(root, "apps", "dashboard", "src", "components", "LandingPage.tsx"));
        Assert.Contains("exclude VAT", landing);
        Assert.Contains("Stripe", landing);
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
