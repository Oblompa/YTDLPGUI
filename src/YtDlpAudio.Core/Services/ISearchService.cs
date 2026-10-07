using YtDlpAudio.Core.Models;

namespace YtDlpAudio.Core.Services;

public enum SearchFilterType
{
    Tracks,
    Playlists
}

public interface ISearchService
{
    Task<IReadOnlyList<SearchResultItem>> SearchAsync(
        string query,
        SearchFilterType filter = SearchFilterType.Tracks,
        int maxResults = 20,
        CancellationToken ct = default
    );
}
