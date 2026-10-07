using System.Globalization;
using System.Text.RegularExpressions;
using YtDlpAudio.Core.Models;

namespace YtDlpAudio.Infrastructure.Process;

public static partial class RegexPatterns
{
    // Matches: [download]  45.2% of ~120.50MiB at 12.4MiB/s ETA 00:05
    // or:      [download]  100% of 15.00MiB in 00:01
    [GeneratedRegex(@"\[download\]\s+(\d+(?:\.\d+)?)%\s+of\s+~?([0-9\.]+[A-Za-z]+)\s+at\s+([0-9\.]+[A-Za-z]+/s)\s+ETA\s+(\d+:\d+)", RegexOptions.Compiled)]
    public static partial Regex DownloadProgressRegex();

    [GeneratedRegex(@"\[download\]\s+100%\s+of\s+~?([0-9\.]+[A-Za-z]+)", RegexOptions.Compiled)]
    public static partial Regex DownloadCompletedRegex();

    [GeneratedRegex(@"\[ExtractAudio\]", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    public static partial Regex ExtractAudioRegex();

    [GeneratedRegex(@"\[(?:Metadata|EmbedMetadata)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    public static partial Regex MetadataRegex();

    [GeneratedRegex(@"\[(?:Thumbnails|EmbedThumbnail)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    public static partial Regex ThumbnailRegex();

    public static bool TryParseProgressLine(
        string line,
        string videoId,
        out DownloadProgressUpdate? update)
    {
        update = null;
        if (string.IsNullOrWhiteSpace(line)) return false;

        var progressMatch = DownloadProgressRegex().Match(line);
        if (progressMatch.Success)
        {
            if (double.TryParse(progressMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double percent))
            {
                string size = progressMatch.Groups[2].Value;
                string speed = progressMatch.Groups[3].Value;
                string eta = progressMatch.Groups[4].Value;

                update = new DownloadProgressUpdate(
                    VideoId: videoId,
                    Percent: percent,
                    DownloadSpeed: speed,
                    Eta: eta,
                    TotalSize: size,
                    State: DownloadState.Downloading
                );
                return true;
            }
        }

        var completedMatch = DownloadCompletedRegex().Match(line);
        if (completedMatch.Success)
        {
            string size = completedMatch.Groups[1].Value;
            update = new DownloadProgressUpdate(
                VideoId: videoId,
                Percent: 100.0,
                DownloadSpeed: "Complete",
                Eta: "00:00",
                TotalSize: size,
                State: DownloadState.Completed
            );
            return true;
        }

        if (ExtractAudioRegex().IsMatch(line))
        {
            update = new DownloadProgressUpdate(
                VideoId: videoId,
                Percent: 100.0,
                DownloadSpeed: "Transcoding",
                Eta: "--:--",
                TotalSize: string.Empty,
                State: DownloadState.ExtractingAudio
            );
            return true;
        }

        if (MetadataRegex().IsMatch(line))
        {
            update = new DownloadProgressUpdate(
                VideoId: videoId,
                Percent: 100.0,
                DownloadSpeed: "Tagging",
                Eta: "--:--",
                TotalSize: string.Empty,
                State: DownloadState.Tagging
            );
            return true;
        }

        if (ThumbnailRegex().IsMatch(line))
        {
            update = new DownloadProgressUpdate(
                VideoId: videoId,
                Percent: 100.0,
                DownloadSpeed: "Embedding Art",
                Eta: "--:--",
                TotalSize: string.Empty,
                State: DownloadState.EmbeddingThumbnail
            );
            return true;
        }

        return false;
    }
}
