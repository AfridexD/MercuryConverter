using System.Text.Json;

namespace Mercury.Converter.Core;

/// <summary>A newer version of the app found on GitHub.</summary>
public sealed record UpdateInfo(Version Version, string PageUrl)
{
    public string Text => $"{Version.Major}.{Version.Minor}.{Version.Build}";
}

/// <summary>
/// Asks GitHub for the latest release. The only thing sent is an ordinary web request, with no personal data.
/// It never downloads or runs anything: it only reports that a newer version exists.
/// </summary>
public static class UpdateChecker
{
    public const string LatestReleaseUrl = "https://api.github.com/repos/AfridexD/MercuryConverter/releases/latest";

    /// <summary>"v1.2.0" or "1.2" becomes a version. Anything else, such as a beta tag, gives null.</summary>
    public static Version? ParseVersion(string? tag)
    {
        var text = tag?.Trim().TrimStart('v', 'V');
        if (string.IsNullOrEmpty(text) || !Version.TryParse(text, out var version))
        {
            return null;
        }

        return new Version(version.Major, Math.Max(0, version.Minor), Math.Max(0, version.Build));
    }

    /// <summary>Reads GitHub's "latest release" answer. Returns null for drafts, pre-releases, and anything unreadable.</summary>
    public static UpdateInfo? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True
                || root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True)
            {
                return null;
            }

            var version = ParseVersion(root.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null);
            var url = root.TryGetProperty("html_url", out var html) ? html.GetString() : null;

            // Only ever send the user to a page on this project's GitHub.
            if (version is null || url is null || !url.StartsWith("https://github.com/AfridexD/MercuryConverter/", StringComparison.Ordinal))
            {
                return null;
            }

            return new UpdateInfo(version, url);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool IsNewer(Version latest, Version current) => latest > current;

    /// <summary>Returns the newer version, or null if this one is current, or if there is no internet or GitHub does not answer.</summary>
    public static async Task<UpdateInfo?> CheckAsync(Version current, HttpClient http, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
            request.Headers.UserAgent.ParseAdd("MercuryConverter-update-check");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var info = Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return info is not null && IsNewer(info.Version, current) ? info : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return null;
        }
    }
}
