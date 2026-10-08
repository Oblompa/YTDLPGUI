using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YtDlpAudio.Core.Models;

namespace YtDlpAudio.Infrastructure.Process;

public interface IYouTubeMusicPlaylistClient
{
    Task<IReadOnlyList<SearchResultItem>> GetLibraryPlaylistsAsync(
        string netscapeCookies,
        CancellationToken ct = default);
}

public sealed class YouTubeMusicPlaylistClient : IYouTubeMusicPlaylistClient
{
    private const string MusicOrigin = "https://music.youtube.com";
    private const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36";
    private const string LibraryBrowseId = "FEmusic_liked_playlists";
    private const int MaximumContinuationPages = 100;
    private static readonly Regex YtConfigSetRegex = new(
        @"ytcfg\.set\s*\(\s*(\{.*?\})\s*\)\s*;",
        RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex PlaylistCountRegex = new(
        @"^\s*([\d][\d,.\s]*)\s*(?:songs?|tracks?|videos?|items?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private readonly Func<HttpMessageHandler>? _handlerFactory;

    public YouTubeMusicPlaylistClient(Func<HttpMessageHandler>? handlerFactory = null)
    {
        _handlerFactory = handlerFactory;
    }

    public async Task<IReadOnlyList<SearchResultItem>> GetLibraryPlaylistsAsync(
        string netscapeCookies,
        CancellationToken ct = default)
    {
        var cookies = ParseCookies(netscapeCookies);
        Uri homeUri = new(MusicOrigin);
        string? sapisid = cookies
            .Where(cookie => string.Equals(cookie.Name, "__Secure-3PAPISID", StringComparison.Ordinal))
            .Select(cookie => cookie.Value)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(sapisid))
        {
            throw new InvalidOperationException(
                "The YouTube Music authentication cookie is unavailable. Sign in again and retry.");
        }

        string authorization = CreateAuthorizationHeader(sapisid, MusicOrigin);
        using var handler = _handlerFactory?.Invoke() ?? new HttpClientHandler
        {
            AllowAutoRedirect = false
        };
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        using var homeRequest = new HttpRequestMessage(HttpMethod.Get, homeUri);
        AddRequestHeaders(homeRequest, cookies, homeUri, authorization);
        using var homeResponse = await client.SendAsync(homeRequest, ct);
        if (!homeResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"YouTube Music could not initialize the playlist request (HTTP {(int)homeResponse.StatusCode}).");
        }

        string homeHtml = await homeResponse.Content.ReadAsStringAsync(ct);
        if (!TryReadClientConfiguration(homeHtml, out var apiKey, out var visitorId, out var clientVersion))
        {
            throw new InvalidOperationException(
                "YouTube Music did not provide the configuration needed to load the library.");
        }

        var requestBody = JsonSerializer.Serialize(new
        {
            context = new
            {
                client = new
                {
                    clientName = "WEB_REMIX",
                    clientVersion,
                    hl = "en",
                    gl = "US"
                },
                user = new { }
            },
            browseId = LibraryBrowseId
        });

        var playlists = new List<SearchResultItem>();
        var seenPlaylistIds = new HashSet<string>(StringComparer.Ordinal);
        var seenContinuationTokens = new HashSet<string>(StringComparer.Ordinal);
        Uri browseUri = BuildBrowseUri(apiKey, continuationToken: null);
        for (int page = 0; page < MaximumContinuationPages; page++)
        {
            using var browseRequest = new HttpRequestMessage(HttpMethod.Post, browseUri)
            {
                Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
            };
            AddRequestHeaders(browseRequest, cookies, browseUri, authorization);
            if (!string.IsNullOrWhiteSpace(visitorId))
            {
                browseRequest.Headers.TryAddWithoutValidation("X-Goog-Visitor-Id", visitorId);
            }

            using var browseResponse = await client.SendAsync(browseRequest, ct);
            if (!browseResponse.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"YouTube Music could not load the playlist library (HTTP {(int)browseResponse.StatusCode}).");
            }

            await using var responseStream = await browseResponse.Content.ReadAsStreamAsync(ct);
            using var response = await JsonDocument.ParseAsync(responseStream, cancellationToken: ct);
            foreach (var playlist in ParsePlaylists(response.RootElement))
            {
                if (seenPlaylistIds.Add(playlist.Id))
                {
                    playlists.Add(playlist);
                }
            }

            string? continuationToken = FindGridContinuationToken(response.RootElement);
            if (continuationToken is null)
            {
                return playlists;
            }

            if (!seenContinuationTokens.Add(continuationToken))
            {
                throw new InvalidOperationException(
                    "YouTube Music returned a repeated playlist continuation; the library could not be fully loaded.");
            }

            browseUri = BuildBrowseUri(apiKey, continuationToken);
        }

        throw new InvalidOperationException(
            $"YouTube Music exceeded the {MaximumContinuationPages}-page playlist browsing safety limit.");
    }

    private static Uri BuildBrowseUri(string apiKey, string? continuationToken)
    {
        string query = $"alt=json&key={Uri.EscapeDataString(apiKey)}";
        if (continuationToken is not null)
        {
            string escapedToken = Uri.EscapeDataString(continuationToken);
            query += $"&ctoken={escapedToken}&continuation={escapedToken}";
        }

        return new Uri($"{MusicOrigin}/youtubei/v1/browse?{query}");
    }

    private static List<NetscapeCookie> ParseCookies(string netscapeCookies)
    {
        var cookies = new List<NetscapeCookie>();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        foreach (string originalLine in netscapeCookies.Split(
                     new[] { '\r', '\n' },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            string line = originalLine.StartsWith("#HttpOnly_", StringComparison.OrdinalIgnoreCase)
                ? originalLine["#HttpOnly_".Length..]
                : originalLine;
            if (line.StartsWith('#') || string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] fields = line.Split('\t');
            if (fields.Length < 7)
            {
                continue;
            }

            string domain = fields[0].Trim().TrimStart('.').ToLowerInvariant();
            if (!IsYouTubeDomain(domain) ||
                !bool.TryParse(fields[1], out bool includeSubdomains) ||
                !bool.TryParse(fields[3], out bool isSecure) ||
                !long.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out long expires) ||
                (expires != 0 && expires <= now))
            {
                continue;
            }

            string name = fields[5].Trim();
            string value = fields[6].Trim();
            string path = string.IsNullOrWhiteSpace(fields[2]) ? "/" : fields[2];
            if (name.Length == 0 || value.Length == 0)
            {
                continue;
            }

            cookies.Add(new NetscapeCookie(domain, includeSubdomains, path, isSecure, name, value));
        }

        return cookies;
    }

    private static bool IsYouTubeDomain(string domain) =>
        domain.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) ||
        domain.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase);

    private static void AddRequestHeaders(
        HttpRequestMessage request,
        IReadOnlyList<NetscapeCookie> cookies,
        Uri uri,
        string authorization)
    {
        request.Headers.UserAgent.ParseAdd(BrowserUserAgent);
        request.Headers.Accept.ParseAdd("*/*");
        request.Headers.TryAddWithoutValidation("Origin", MusicOrigin);
        request.Headers.TryAddWithoutValidation("X-Origin", MusicOrigin);
        request.Headers.TryAddWithoutValidation("X-Goog-AuthUser", "0");
        request.Headers.TryAddWithoutValidation("Authorization", authorization);

        string cookieHeader = string.Join(
            "; ",
            cookies
                .Where(cookie => CookieAppliesTo(cookie, uri))
                .GroupBy(cookie => cookie.Name, StringComparer.Ordinal)
                .Select(group => group.First())
                .Select(cookie => $"{cookie.Name}={cookie.Value}"));
        if (cookieHeader.Length > 0)
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
        }
    }

    private static bool CookieAppliesTo(NetscapeCookie cookie, Uri uri)
    {
        string host = uri.Host.ToLowerInvariant();
        bool domainMatches = cookie.IncludeSubdomains
            ? host.Equals(cookie.Domain, StringComparison.OrdinalIgnoreCase) ||
              host.EndsWith($".{cookie.Domain}", StringComparison.OrdinalIgnoreCase)
            : host.Equals(cookie.Domain, StringComparison.OrdinalIgnoreCase);
        bool pathMatches = uri.AbsolutePath.StartsWith(cookie.Path, StringComparison.Ordinal) &&
                           (cookie.Path.EndsWith('/') ||
                            uri.AbsolutePath.Length == cookie.Path.Length ||
                            uri.AbsolutePath[cookie.Path.Length] == '/');
        return domainMatches && pathMatches && (!cookie.Secure || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static string CreateAuthorizationHeader(string sapisid, string origin)
    {
        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            .ToString(CultureInfo.InvariantCulture);
        byte[] input = Encoding.UTF8.GetBytes($"{timestamp} {sapisid} {origin}");
        string signature = Convert.ToHexString(SHA1.HashData(input)).ToLowerInvariant();
        return $"SAPISIDHASH {timestamp}_{signature}";
    }

    private static bool TryReadClientConfiguration(
        string html,
        out string apiKey,
        out string? visitorId,
        out string clientVersion)
    {
        apiKey = string.Empty;
        visitorId = null;
        clientVersion = $"1.{DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.01.00";

        foreach (Match match in YtConfigSetRegex.Matches(html))
        {
            try
            {
                using var config = JsonDocument.Parse(match.Groups[1].Value);
                JsonElement root = config.RootElement;
                if (!TryGetString(root, "INNERTUBE_API_KEY", out string? foundApiKey))
                {
                    continue;
                }

                apiKey = foundApiKey;
                TryGetString(root, "VISITOR_DATA", out visitorId);
                if (TryGetString(root, "INNERTUBE_CLIENT_VERSION", out string? foundVersion))
                {
                    clientVersion = foundVersion;
                }
                else if (TryGetStringPath(
                             root,
                             out foundVersion,
                             "INNERTUBE_CONTEXT",
                             "client",
                             "clientVersion"))
                {
                    clientVersion = foundVersion;
                }

                return true;
            }
            catch (JsonException)
            {
                // A page may contain unrelated or malformed ytcfg calls; inspect the next one.
            }
        }

        return false;
    }

    private static IReadOnlyList<SearchResultItem> ParsePlaylists(JsonElement root)
    {
        var results = new List<SearchResultItem>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        FindPlaylistRenderers(root, results, seenIds);
        return results;
    }

    private static string? FindGridContinuationToken(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.Name is "gridRenderer" or "gridContinuation")
                {
                    JsonElement continuation = GetPath(
                        property.Value,
                        "continuations",
                        "0",
                        "nextContinuationData",
                        "continuation");
                    if (continuation.ValueKind == JsonValueKind.String)
                    {
                        return continuation.GetString();
                    }
                }

                string? nested = FindGridContinuationToken(property.Value);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray())
            {
                string? nested = FindGridContinuationToken(child);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static void FindPlaylistRenderers(
        JsonElement element,
        List<SearchResultItem> results,
        HashSet<string> seenIds)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if ((property.Name is "musicTwoRowItemRenderer" or "gridPlaylistRenderer") &&
                    TryParsePlaylist(property.Value, out SearchResultItem? playlist) &&
                    playlist is not null &&
                    seenIds.Add(playlist.Id))
                {
                    results.Add(playlist);
                }

                FindPlaylistRenderers(property.Value, results, seenIds);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray())
            {
                FindPlaylistRenderers(child, results, seenIds);
            }
        }
    }

    private static bool TryParsePlaylist(JsonElement renderer, out SearchResultItem? playlist)
    {
        playlist = null;
        string? title = GetRunsText(GetPath(renderer, "title"));
        if (string.IsNullOrWhiteSpace(title))
        {
            title = GetStringPath(renderer, "title", "simpleText");
        }

        JsonElement firstTitleRun = GetFirstArrayElement(GetPath(renderer, "title", "runs"));
        string? browseId = GetStringPath(
            firstTitleRun,
            "navigationEndpoint",
            "browseEndpoint",
            "browseId");
        string? id = browseId?.StartsWith("VL", StringComparison.Ordinal) == true
            ? browseId[2..]
            : FindStringProperty(renderer, "playlistId");

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        int? trackCount = GetTrackCount(renderer);
        string? thumbnailUrl = FindThumbnailUrl(renderer);
        playlist = new SearchResultItem(
            Id: id,
            Title: title,
            Author: "YouTube Music",
            Duration: null,
            TrackCount: trackCount,
            ThumbnailUrl: thumbnailUrl,
            Url: $"https://www.youtube.com/playlist?list={Uri.EscapeDataString(id)}",
            ResultType: SearchResultType.Playlist,
            Source: "YouTube Music");
        return true;
    }

    private static int? GetTrackCount(JsonElement renderer)
    {
        JsonElement subtitleRuns = GetPath(renderer, "subtitle", "runs");
        if (subtitleRuns.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement run in subtitleRuns.EnumerateArray().Reverse())
        {
            if (!TryGetString(run, "text", out string? text))
            {
                continue;
            }

            Match match = PlaylistCountRegex.Match(text);
            if (!match.Success)
            {
                continue;
            }

            string digits = Regex.Replace(match.Groups[1].Value, @"\D", string.Empty);
            if (int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int count))
            {
                return count;
            }
        }

        return null;
    }

    private static string? FindThumbnailUrl(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.Name == "thumbnails" && property.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement thumbnail in property.Value.EnumerateArray().Reverse())
                    {
                        if (TryGetString(thumbnail, "url", out string? url))
                        {
                            return url;
                        }
                    }
                }

                string? nested = FindThumbnailUrl(property.Value);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray())
            {
                string? nested = FindThumbnailUrl(child);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static string? FindStringProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.Name == propertyName && property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }

                string? nested = FindStringProperty(property.Value, propertyName);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray())
            {
                string? nested = FindStringProperty(child, propertyName);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static string? GetRunsText(JsonElement element)
    {
        JsonElement runs = GetPath(element, "runs");
        if (runs.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var text = new StringBuilder();
        foreach (JsonElement run in runs.EnumerateArray())
        {
            if (TryGetString(run, "text", out string? runText))
            {
                text.Append(runText);
            }
        }

        return text.Length == 0 ? null : text.ToString();
    }

    private static JsonElement GetFirstArrayElement(JsonElement element) =>
        element.ValueKind == JsonValueKind.Array && element.GetArrayLength() > 0
            ? element[0]
            : default;

    private static JsonElement GetPath(JsonElement element, params string[] path)
    {
        foreach (string segment in path)
        {
            if (element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(segment, out var property))
            {
                element = property;
            }
            else if (element.ValueKind == JsonValueKind.Array &&
                     int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out int index) &&
                     index >= 0 &&
                     index < element.GetArrayLength())
            {
                element = element[index];
            }
            else
            {
                return default;
            }
        }

        return element;
    }

    private static bool TryGetStringPath(
        JsonElement element,
        [NotNullWhen(true)] out string? value,
        params string[] path)
    {
        JsonElement target = GetPath(element, path);
        value = target.ValueKind == JsonValueKind.String ? target.GetString() : null;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string? GetStringPath(JsonElement element, params string[] path) =>
        TryGetStringPath(element, out string? value, path) ? value : null;

    private static bool TryGetString(
        JsonElement element,
        string propertyName,
        [NotNullWhen(true)] out string? value)
    {
        value = null;
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private sealed record NetscapeCookie(
        string Domain,
        bool IncludeSubdomains,
        string Path,
        bool Secure,
        string Name,
        string Value);
}
