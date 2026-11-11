using FieldKit.Services;
using Xunit;

namespace FieldKit.Tests;

public class AppCatalogTests
{
    [Fact]
    public void Embedded_catalog_loads_and_is_consistent()
    {
        var catalog = AppCatalog.Load();

        Assert.NotEmpty(catalog.Apps);
        Assert.NotEmpty(catalog.Bundles);
        Assert.All(catalog.Apps, app =>
        {
            Assert.False(string.IsNullOrWhiteSpace(app.Name));
            Assert.False(string.IsNullOrWhiteSpace(app.Group));
            // Every ID must be one winget will accept as an exact match.
            WingetService.BuildPackageArguments("install", app.Id);
        });
    }

    [Fact]
    public void Every_bundle_has_at_least_one_app()
    {
        var catalog = AppCatalog.Load();
        Assert.All(catalog.Bundles, bundle =>
            Assert.Contains(catalog.Apps, app => app.Bundles.Contains(bundle.Key)));
    }

    [Fact]
    public void Links_use_https_or_the_store_scheme_only()
    {
        var catalog = AppCatalog.Load();
        Assert.All(catalog.Links, link =>
            Assert.True(link.Url.StartsWith("https://", StringComparison.Ordinal)
                        || link.Url.StartsWith("ms-windows-store://", StringComparison.Ordinal), link.Url));
    }

    [Fact]
    public void Duplicate_ids_are_rejected()
    {
        const string json = """
            { "apps": [
                { "id": "A.B", "name": "One", "group": "G", "note": "", "bundles": [] },
                { "id": "a.b", "name": "Two", "group": "G", "note": "", "bundles": [] } ],
              "bundles": [], "links": [] }
            """;
        Assert.Throws<InvalidOperationException>(() => AppCatalog.Parse(json));
    }

    [Fact]
    public void Undefined_bundles_are_rejected()
    {
        const string json = """
            { "apps": [ { "id": "A.B", "name": "One", "group": "G", "note": "", "bundles": ["missing"] } ],
              "bundles": [], "links": [] }
            """;
        Assert.Throws<InvalidOperationException>(() => AppCatalog.Parse(json));
    }
}
