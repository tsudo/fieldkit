using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace FieldKit.Services;

internal static class AboutContent
{
    internal const string Repository = "https://github.com/tsudo/fieldkit";
    internal const string Releases = Repository + "/releases";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(12) };

    internal static string Disclaimer => Read("FieldKit.Disclaimer.txt");
    internal static string License => Read("FieldKit.License.txt");

    private static string Read(string name)
    {
        using var stream = typeof(AboutContent).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing bundled text: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    internal static async Task<string> CheckReleaseAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/tsudo/fieldkit/releases/latest");
        request.Headers.UserAgent.ParseAdd("FieldKit-Release-Check/1.0");
        using var response = await Client.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return "GitHub could not find a published release. See Releases on GitHub.";
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            return "GitHub declined the check. Try again later or open Releases on GitHub.";
        response.EnsureSuccessStatusCode();
        using var release = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!release.RootElement.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String)
            return "GitHub returned no release tag. See Releases on GitHub.";
        return Compare(SystemInfo.GetDisplayVersion(), tag.GetString()!);
    }

    internal static string Compare(string installed, string tag)
    {
        var local = installed.Split('+')[0].Split('-', 2);
        var stable = tag.TrimStart('v', 'V').Split('+')[0];
        if (!Version.TryParse(local[0], out var current) || !Version.TryParse(stable, out var latest))
            return $"Latest release tag: {tag}. Compare it with your version above.";
        if (current < latest || (current == latest && local.Length == 2))
            return $"FieldKit {tag} is available. Open Releases on GitHub for the download.";
        return current == latest ? $"You have the latest stable release ({tag})."
            : $"This build is newer than the latest stable release ({tag}).";
    }
}
