namespace YtDlpAudio.Core.Models;

public record TrackMetadata(
    string Id,
    string Title,
    string Artist,
    string? Album,
    TimeSpan Duration,
    string? ThumbnailUrl,
    int? PlaylistIndex,
    string WebpageUrl
);
