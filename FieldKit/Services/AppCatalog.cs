using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FieldKit.Services;

public sealed record CatalogApp(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("group")] string Group,
    [property: JsonPropertyName("note")] string Note,
    [property: JsonPropertyName("bundles")] IReadOnlyList<string> Bundles);

public sealed record CatalogBundle(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("name")] string Name);

public sealed record CatalogLink(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("note")] string Note);

/// <summary>The curated app list shipped inside the exe (Catalog/apps.json).</summary>
public sealed record AppCatalog(
    [property: JsonPropertyName("apps")] IReadOnlyList<CatalogApp> Apps,
    [property: JsonPropertyName("bundles")] IReadOnlyList<CatalogBundle> Bundles,
    [property: JsonPropertyName("links")] IReadOnlyList<CatalogLink> Links)
{
    private const string ResourceName = "FieldKit.Catalog.apps.json";

    public static AppCatalog Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing from the build.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public static AppCatalog Parse(string json)
    {
        var catalog = JsonSerializer.Deserialize<AppCatalog>(json)
                      ?? throw new InvalidOperationException("The app catalog is empty.");

        var duplicate = catalog.Apps.GroupBy(app => app.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"The app catalog lists {duplicate.Key} more than once.");

        var known = catalog.Bundles.Select(bundle => bundle.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = catalog.Apps.SelectMany(app => app.Bundles).FirstOrDefault(key => !known.Contains(key));
        if (unknown is not null)
            throw new InvalidOperationException($"The app catalog uses an undefined bundle: {unknown}.");

        return catalog;
    }
}
