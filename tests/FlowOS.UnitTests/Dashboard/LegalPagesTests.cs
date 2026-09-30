using System.IO;
using System.Linq;
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

        foreach (var file in new[]
        {
            "terms.html", "privacy.html", "refunds.html", "cancellation.html", "acceptable-use.html", "contact.html",
            "product.html", "pricing.html", "security.html", "ai-agents.html", "company.html"
        })
        {
            var html = File.ReadAllText(Path.Combine(root, "apps", "dashboard", "public", file));
            Assert.Contains(operatorName!, html);
            Assert.Contains(email!, html);
            Assert.Contains("href=\"/terms\"", html);
            Assert.Contains("href=\"/privacy\"", html);
            Assert.Contains("href=\"/refunds\"", html);
            Assert.Contains("href=\"/cancellation\"", html);
            Assert.Contains("href=\"/contact\"", html);
            Assert.Contains($"<link rel=\"canonical\" href=\"{site}{PathFromPublicFile(file)}\" />", html);
            Assert.Contains("href=\"/ai-agents\"", html);
            Assert.DoesNotContain("Coming soon", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("XXXX", html);
        }

        var pricing = File.ReadAllText(Path.Combine(root, "apps", "dashboard", "public", "pricing.html"));
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "apps", "dashboard", "src", "pricingCatalog.json")));
        var builder = catalog.RootElement.GetProperty("tiers").EnumerateArray()
            .First(tier => tier.GetProperty("id").GetString() == "builder");
        Assert.Contains($"${builder.GetProperty("monthlyUsd").GetInt32()}", pricing);
        Assert.Contains(builder.GetProperty("name").GetString()!, pricing);
        Assert.Contains("USD", pricing);
        Assert.Contains("application/ld+json", pricing);
        Assert.Contains("\"@type\":\"Offer\"", pricing);
        Assert.DoesNotContain("Organization", pricing);

        var robots = File.ReadAllText(Path.Combine(root, "apps", "dashboard", "public", "robots.txt"));
        Assert.Contains($"Sitemap: {site}/sitemap.xml", robots);
        Assert.Contains("Disallow: /swagger", robots);
        Assert.Contains("Disallow: /health", robots);

        var sitemap = File.ReadAllText(Path.Combine(root, "apps", "dashboard", "public", "sitemap.xml"));
        Assert.Contains($"{site}/", sitemap);
        Assert.Contains($"{site}/pricing", sitemap);
        Assert.Contains($"{site}/product", sitemap);
        Assert.DoesNotContain("/swagger", sitemap);
        Assert.DoesNotContain("/health", sitemap);

        var home = File.ReadAllText(Path.Combine(root, "apps", "dashboard", "index.html"));
        Assert.Contains($"<link rel=\"canonical\" href=\"{site}/\" />", home);
        Assert.DoesNotContain("no-store", home);
        Assert.DoesNotContain("name=\"keywords\"", home);
        Assert.DoesNotContain("648 automated", home);
        Assert.Contains("pricingCatalog.json", File.ReadAllText(Path.Combine(root, "apps", "dashboard", "src", "pricingLadder.ts")));
        Assert.Contains("pricingCatalog.json", File.ReadAllText(Path.Combine(root, "docs", "18-commercial-and-mcp-entitlements.md")));

        var company = File.ReadAllText(Path.Combine(root, "apps", "dashboard", "public", "company.html"));
        if (string.IsNullOrWhiteSpace(rootElement.GetProperty("companyNumber").GetString()))
            Assert.DoesNotContain("Company number:", company);
        if (string.IsNullOrWhiteSpace(rootElement.GetProperty("registrationJurisdiction").GetString()))
            Assert.DoesNotContain("England and Wales", company);

        var landing = File.ReadAllText(Path.Combine(root, "apps", "dashboard", "src", "components", "LandingPage.tsx"));
        Assert.Contains("exclude VAT", landing);
        Assert.Contains("Stripe", landing);
    }

    private static string PathFromPublicFile(string file) =>
        "/" + Path.GetFileNameWithoutExtension(file);

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
