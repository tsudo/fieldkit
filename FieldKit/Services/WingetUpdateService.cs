using System.Text.RegularExpressions;

namespace FieldKit.Services;

public sealed class WingetUpdateService
{
    private static readonly Regex AnsiPattern = new(@"\x1B\[[0-9;?]*[ -/]*[@-~]", RegexOptions.Compiled);

    public IReadOnlyList<WingetPackageCandidate> ParseUpgradeCandidates(IEnumerable<string> lines)
    {
        var cleanLines = lines.Select(CleanLine).Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
        var headerIndex = cleanLines.FindIndex(line =>
            line.Contains("Name", StringComparison.OrdinalIgnoreCase)
            && line.Contains("Id", StringComparison.OrdinalIgnoreCase)
            && line.Contains("Version", StringComparison.OrdinalIgnoreCase)
            && line.Contains("Available", StringComparison.OrdinalIgnoreCase)
            && line.Contains("Source", StringComparison.OrdinalIgnoreCase));

        if (headerIndex < 0)
            return [];

        var header = cleanLines[headerIndex];
        var idStart = header.IndexOf("Id", StringComparison.Ordinal);
        var versionStart = header.IndexOf("Version", StringComparison.Ordinal);
        var availableStart = header.IndexOf("Available", StringComparison.Ordinal);
        var sourceStart = header.IndexOf("Source", StringComparison.Ordinal);
        if (idStart <= 0 || versionStart <= idStart || availableStart <= versionStart || sourceStart <= availableStart)
            return [];

        var candidates = new List<WingetPackageCandidate>();
        foreach (var line in cleanLines.Skip(headerIndex + 1))
        {
            if (line.StartsWith("-", StringComparison.Ordinal)
                || line.Contains("upgrades available", StringComparison.OrdinalIgnoreCase)
                || line.Contains("package(s)", StringComparison.OrdinalIgnoreCase)
                || line.Contains("Installing dependencies", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (line.Length <= sourceStart)
                continue;

            var name = Slice(line, 0, idStart);
            var id = Slice(line, idStart, versionStart);
            var version = Slice(line, versionStart, availableStart);
            var available = Slice(line, availableStart, sourceStart);
            var source = line[sourceStart..].Trim();

            if (string.IsNullOrWhiteSpace(name)
                || string.IsNullOrWhiteSpace(id)
                || string.IsNullOrWhiteSpace(version)
                || string.IsNullOrWhiteSpace(available)
                || !source.Equals("winget", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            candidates.Add(new WingetPackageCandidate(name, id, version, available, source));
        }

        return candidates;
    }

    public static string BuildUpgradeArguments(string packageId) =>
        $"upgrade --id \"{packageId}\" --silent --accept-source-agreements --accept-package-agreements";

    private static string CleanLine(string line) => AnsiPattern.Replace(line, "").TrimEnd();

    private static string Slice(string value, int start, int nextStart)
    {
        if (value.Length <= start)
            return "";

        var length = Math.Min(nextStart, value.Length) - start;
        return length <= 0 ? "" : value.Substring(start, length).Trim();
    }
}
