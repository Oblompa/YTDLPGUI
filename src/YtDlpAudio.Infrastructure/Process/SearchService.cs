using System.Text.Json;
using YtDlpAudio.Core.Models;
using YtDlpAudio.Core.Services;

namespace YtDlpAudio.Infrastructure.Process;

public class SearchService : ISearchService
{
    private readonly IYtDlpRunner _runner;
    private readonly IAuthManager? _authManager;

    public SearchService(IYtDlpRunner runner, IAuthManager? authManager = null)
    {
        _runner = runner;
        _authManager = authManager;
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
        string searchPrefix = filter == SearchFilterType.Playlists
            ? $"ytsearchplaylist{maxResults}:"
            : $"ytsearch{maxResults}:";

        string searchTarget = $"{searchPrefix}{cleanQuery}";

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
            var root = await _runner.QueryMetadataAsync(searchTarget, cookiesPath, isFlatPlaylist: true, ct);
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
