using System.Text.Json;
using YtDlpAudio.Core.Models;

namespace YtDlpAudio.Core.Services;

public interface IYtDlpRunner
{
    Task<YtDlpResult> ExecuteAsync(
        string arguments,
        IProgress<DownloadProgressUpdate>? progress = null,
        CancellationToken ct = default
    );

    Task<JsonElement> QueryMetadataAsync(
        string url,
        string? cookiesFilePath = null,
        bool isFlatPlaylist = true,
        CancellationToken ct = default
    );
}
