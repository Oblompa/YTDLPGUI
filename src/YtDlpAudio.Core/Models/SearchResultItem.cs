namespace YtDlpAudio.Core.Models;

public enum SearchResultType
{
    Track,
    Playlist
}

public record SearchResultItem(
    string Id,
    string Title,
    string Author,
    TimeSpan? Duration,
    int? TrackCount,
    string? ThumbnailUrl,
    string Url,
    SearchResultType ResultType
);
