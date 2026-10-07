namespace YtDlpAudio.Core.Models;

public record AudioPipelineConfig(
    string OutputDirectory,
    AudioQualityPreset QualityPreset = AudioQualityPreset.Cbr320k,
    string AudioFormat = "mp3",
    bool EmbedAlbumArt = true,
    bool EmbedMetadata = true,
    bool CleanNoiseFromTitle = true,
    string FilenameTemplate = "%(artist,uploader)s - %(title)s.%(ext)s"
)
{
    public static AudioPipelineConfig Default => new(
        OutputDirectory: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "YtDlpDownloads")
    );
}
