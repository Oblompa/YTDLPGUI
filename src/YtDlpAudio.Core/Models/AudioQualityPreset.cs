namespace YtDlpAudio.Core.Models;

public enum AudioQualityPreset
{
    /// <summary>
    /// Constant Bitrate 320 kbps (Maximum compatibility and fidelity for hardware players)
    /// </summary>
    Cbr320k,

    /// <summary>
    /// Variable Bitrate V0 (Optimal psychoacoustic compression, ~245 kbps)
    /// </summary>
    VbrV0,

    /// <summary>
    /// Moderate Bitrate 192 kbps (Smaller file size)
    /// </summary>
    Cbr192k
}

public static class AudioQualityPresetExtensions
{
    public static string ToYtDlpQualityArgument(this AudioQualityPreset preset) => preset switch
    {
        AudioQualityPreset.Cbr320k => "320k",
        AudioQualityPreset.VbrV0 => "0",
        AudioQualityPreset.Cbr192k => "192k",
        _ => "0"
    };
}
