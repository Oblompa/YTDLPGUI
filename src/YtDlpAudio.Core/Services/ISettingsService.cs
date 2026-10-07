using YtDlpAudio.Core.Models;

namespace YtDlpAudio.Core.Services;

public record UserSettings(
    string OutputDirectory = "",
    AudioQualityPreset QualityPreset = AudioQualityPreset.Cbr320k,
    bool EmbedAlbumArt = true,
    bool EmbedMetadata = true,
    bool CleanTitle = true,
    SearchFilterType SearchFilter = SearchFilterType.Tracks
);

public interface ISettingsService
{
    Task<UserSettings> LoadSettingsAsync(CancellationToken ct = default);
    Task SaveSettingsAsync(UserSettings settings, CancellationToken ct = default);
}
