using System.Text;
using YtDlpAudio.Core.Models;

namespace YtDlpAudio.Infrastructure.Process;

public class YtDlpArgumentBuilder
{
    private readonly List<string> _arguments = new();

    public YtDlpArgumentBuilder WithFfmpegLocation(string ffmpegDirectory)
    {
        _arguments.Add($"--ffmpeg-location \"{ffmpegDirectory}\"");
        return this;
    }

    public YtDlpArgumentBuilder WithCookies(string? cookieFilePath)
    {
        if (!string.IsNullOrWhiteSpace(cookieFilePath) && File.Exists(cookieFilePath))
        {
            _arguments.Add($"--cookies \"{cookieFilePath}\"");
        }
        return this;
    }

    public YtDlpArgumentBuilder WithAudioOnly(string format = "mp3", AudioQualityPreset quality = AudioQualityPreset.Cbr320k)
    {
        _arguments.Add("-x");
        _arguments.Add($"--audio-format {format}");
        _arguments.Add($"--audio-quality {quality.ToYtDlpQualityArgument()}");
        return this;
    }

    public YtDlpArgumentBuilder WithStreamSelection(bool preferPremium = false)
    {
        if (preferPremium)
        {
            // format 141 is YouTube Premium 256k AAC. Fallback to bestaudio
            _arguments.Add("-f \"ba[format_id=141]/ba\"");
        }
        else
        {
            // Guest / Standard: best audio stream (Opus 160k or AAC 128k)
            _arguments.Add("-f \"ba\"");
        }
        return this;
    }

    public YtDlpArgumentBuilder WithEmbeddings()
    {
        _arguments.Add("--embed-metadata");
        _arguments.Add("--embed-thumbnail");
        _arguments.Add("--convert-thumbnails jpg");
        return this;
    }

    public YtDlpArgumentBuilder WithOutputTemplate(string template)
    {
        _arguments.Add($"-o \"{template}\"");
        _arguments.Add("--windows-filenames");
        return this;
    }

    public YtDlpArgumentBuilder WithFlatPlaylist()
    {
        _arguments.Add("--flat-playlist");
        return this;
    }

    public YtDlpArgumentBuilder WithDumpJson()
    {
        _arguments.Add("--dump-single-json");
        return this;
    }

    public YtDlpArgumentBuilder WithNoWarnings()
    {
        _arguments.Add("--no-warnings");
        return this;
    }

    public string Build(string target)
    {
        var sb = new StringBuilder();
        foreach (var arg in _arguments)
        {
            sb.Append(arg).Append(' ');
        }
        sb.Append($"\"{target}\"");
        return sb.ToString().Trim();
    }
}
