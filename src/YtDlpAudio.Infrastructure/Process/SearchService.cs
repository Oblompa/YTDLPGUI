using System.Text.Json;
using System.Text.RegularExpressions;
using YtDlpAudio.Core.Models;
using YtDlpAudio.Core.Services;

namespace YtDlpAudio.Infrastructure.Process;

public class SearchService : ISearchService
{
    private const string AccountPlaylistsUrl = "https://www.youtube.com/feed/playlists";

    private readonly IYtDlpRunner _runner;
    private readonly IAuthManager? _authManager;
    private readonly IYouTubeMusicPlaylistClient _youtubeMusicPlaylistClient;

    public SearchService(
        IYtDlpRunner runner,
        IAuthManager? authManager = null,
        IYouTubeMusicPlaylistClient? youtubeMusicPlaylistClient = null)
    {
        _runner = runner;
        _authManager = authManager;
        _youtubeMusicPlaylistClient = youtubeMusicPlaylistClient ?? new YouTubeMusicPlaylistClient();
    }

    public async Task<IReadOnlyList<SearchResultItem>> SearchAsync(
        string query,
        SearchFilterType filter = SearchFilterType.Tracks,
        int maxResults = 20,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<SearchResultItem>();
        }

        string cleanQuery = query.Trim();
        string searchTarget;
        int? playlistEnd = null;
        if (filter == SearchFilterType.Playlists)
        {
            var searchUrl = new UriBuilder("https://www.youtube.com/results")
            {
                Query = $"search_query={Uri.EscapeDataString(cleanQuery)}&sp=EgIQAw%3D%3D"
            };
            searchTarget = searchUrl.Uri.AbsoluteUri;
            playlistEnd = maxResults;
        }
        else
        {
            searchTarget = $"ytsearch{maxResults}:{cleanQuery}";
        }

        string? cookiesPath = null;
        if (_authManager != null)
        {
            var auth = await _authManager.GetCurrentAuthStatusAsync(ct);
            if (auth.IsLoggedIn)
            {
                cookiesPath = await _authManager.ExportCookiesToTempFileAsync(ct);
            }
        }

        try
        {
            var root = await _runner.QueryMetadataAsync(
                searchTarget,
                cookiesPath,
                isFlatPlaylist: true,
                ct,
                playlistEnd);
            return ParseSearchResults(root, filter);
        }
        finally
        {
            if (cookiesPath != null && File.Exists(cookiesPath))
            {
                try { File.Delete(cookiesPath); } catch { /* best effort temp cleanup */ }
            }
        }
    }

    public async Task<IReadOnlyList<SearchResultItem>> BrowseAccountPlaylistsAsync(
        CancellationToken ct = default)
    {
        string cookiesPath = await ExportAccountCookiesAsync(ct);
        try
        {
            var root = await _runner.QueryMetadataAsync(
                AccountPlaylistsUrl,
                cookiesPath,
                isFlatPlaylist: true,
                ct);

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("entries", out var entries) ||
                entries.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException(
                    "YouTube did not return a complete playlist list. Your session may have expired; sign in again and retry.");
            }

            return ParseSearchResults(root, SearchFilterType.Playlists)
                .Select(playlist => playlist with { Source = "YouTube" })
                .ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            string detail = SanitizeAccountLibraryError(ex.Message);
            throw new InvalidOperationException(
                $"Could not load your YouTube playlist library. Your session may have expired or access may be denied. {detail}",
                ex);
        }
        finally
        {
            TryDeleteCookieExport(cookiesPath);
        }
    }

    public async Task<IReadOnlyList<SearchResultItem>> BrowseYouTubeMusicPlaylistsAsync(
        CancellationToken ct = default)
    {
        string cookiesPath = await ExportAccountCookiesAsync(ct);
        try
        {
            string cookies = await File.ReadAllTextAsync(cookiesPath, ct);
            var playlists = await _youtubeMusicPlaylistClient.GetLibraryPlaylistsAsync(cookies, ct);
            return playlists
                .Select(playlist => playlist with { Source = "YouTube Music" })
                .ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            string detail = SanitizeAccountLibraryError(ex.Message);
            throw new InvalidOperationException(
                $"Could not load your YouTube Music playlist library. Your session may have expired or access may be denied. {detail}",
                ex);
        }
        finally
        {
            TryDeleteCookieExport(cookiesPath);
        }
    }

    private async Task<string> ExportAccountCookiesAsync(CancellationToken ct)
    {
        if (_authManager is null)
        {
            throw new InvalidOperationException("Sign in to browse playlists in your YouTube account.");
        }

        var authStatus = await _authManager.GetCurrentAuthStatusAsync(ct);
        if (!authStatus.IsLoggedIn)
        {
            throw new InvalidOperationException("Sign in to browse playlists in your YouTube account.");
        }

        return await _authManager.ExportCookiesToTempFileAsync(ct);
    }

    private static void TryDeleteCookieExport(string cookiesPath)
    {
        try
        {
            if (File.Exists(cookiesPath))
            {
                File.Delete(cookiesPath);
            }
        }
        catch
        {
            // Best-effort removal of the temporary authentication export.
        }
    }

    private static string SanitizeAccountLibraryError(string message)
    {
        string sanitized = Regex.Replace(message, @"https?://[^\s""'<>]+", "[URL]", RegexOptions.IgnoreCase);
        sanitized = Regex.Replace(
            sanitized,
            @"(?im)\b(authorization|proxy-authorization|cookie|cookies|set-cookie)\b\s*[:=][^\r\n]*",
            "$1=[REDACTED]");
        sanitized = Regex.Replace(
            sanitized,
            @"(?i)\b(access[_-]?token|refresh[_-]?token|token|sapisid|apisid|sid|hsid|ssid|login_info|visitor_info1_live|ysc|__secure-[\w-]+)\b\s*=\s*[^\s,;""}]+",
            "$1=[REDACTED]");
        return sanitized;
    }

    private static IReadOnlyList<SearchResultItem> ParseSearchResults(JsonElement root, SearchFilterType filter)
    {
        var items = new List<SearchResultItem>();

        if (!root.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            // Single entry fallback
            var singleItem = ParseSingleEntry(root, filter);
            if (singleItem != null) items.Add(singleItem);
            return items;
        }

        foreach (var entry in entries.EnumerateArray())
        {
            var item = ParseSingleEntry(entry, filter);
            if (item != null)
            {
                items.Add(item);
            }
        }

        return items;
    }

    private static SearchResultItem? ParseSingleEntry(JsonElement entry, SearchFilterType filter)
    {
        if (entry.ValueKind != JsonValueKind.Object) return null;

        string id = entry.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty;
        string title = entry.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? "Unknown Title" : "Unknown Title";
        
        string author = "Unknown Artist";
        if (entry.TryGetProperty("uploader", out var uploaderProp) && !string.IsNullOrWhiteSpace(uploaderProp.GetString()))
        {
            author = uploaderProp.GetString()!;
        }
        else if (entry.TryGetProperty("channel", out var channelProp) && !string.IsNullOrWhiteSpace(channelProp.GetString()))
        {
            author = channelProp.GetString()!;
        }

        TimeSpan? duration = null;
        if (entry.TryGetProperty("duration", out var durProp) && durProp.ValueKind == JsonValueKind.Number && durProp.TryGetDouble(out double durSec))
        {
            duration = TimeSpan.FromSeconds(durSec);
        }

        int? trackCount = null;
        if (entry.TryGetProperty("playlist_count", out var countProp) && countProp.ValueKind == JsonValueKind.Number && countProp.TryGetInt32(out int count))
        {
            trackCount = count;
        }

        string? thumbnail = null;
        if (entry.TryGetProperty("thumbnails", out var thumbsProp) && thumbsProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var thumb in thumbsProp.EnumerateArray())
            {
                if (thumb.TryGetProperty("url", out var urlProp))
                {
                    thumbnail = urlProp.GetString();
                }
            }
        }
        else if (entry.TryGetProperty("thumbnail", out var thumbProp))
        {
            thumbnail = thumbProp.GetString();
        }

        string url = entry.TryGetProperty("url", out var urlElem) ? urlElem.GetString() ?? string.Empty : string.Empty;
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            url = filter == SearchFilterType.Playlists
                ? $"https://www.youtube.com/playlist?list={id}"
                : $"https://www.youtube.com/watch?v={id}";
        }

        var resultType = filter == SearchFilterType.Playlists
            ? SearchResultType.Playlist
            : SearchResultType.Track;

        return new SearchResultItem(
            Id: id,
            Title: title,
            Author: author,
            Duration: duration,
            TrackCount: trackCount,
            ThumbnailUrl: thumbnail,
            Url: url,
            ResultType: resultType
        );
    }
}
