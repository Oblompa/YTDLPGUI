using System.Text.Json;
using System.Text.RegularExpressions;
using YtDlpAudio.Core.Models;
using YtDlpAudio.Core.Services;

namespace YtDlpAudio.Core.Tests;

public class MockYtDlpRunner : IYtDlpRunner
{
    public Func<string, IProgress<DownloadProgressUpdate>?, CancellationToken, Task<YtDlpResult>>? ExecuteHandler { get; set; }
    public Func<string, string?, bool, CancellationToken, Task<JsonElement>>? QueryMetadataHandler { get; set; }
    public int? LastQueryPlaylistEnd { get; private set; }

    public async Task<YtDlpResult> ExecuteAsync(
        string arguments,
        IProgress<DownloadProgressUpdate>? progress = null,
        CancellationToken ct = default)
    {
        if (ExecuteHandler != null)
        {
            return await ExecuteHandler(arguments, progress, ct);
        }

        // Default simulation
        for (int p = 10; p <= 100; p += 30)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(20, ct);
            progress?.Report(new DownloadProgressUpdate(
                VideoId: "mock_vid",
                Percent: p,
                DownloadSpeed: "12.5MiB/s",
                Eta: "00:03",
                TotalSize: "8.20MiB",
                State: p == 100 ? DownloadState.Completed : DownloadState.Downloading
            ));
        }

        var outputTemplateMatch = Regex.Match(arguments, "-o\\s+\"([^\"]+)\"");
        string? outputPath = null;
        if (outputTemplateMatch.Success)
        {
            string? outputDirectory = Path.GetDirectoryName(outputTemplateMatch.Groups[1].Value);
            if (!string.IsNullOrEmpty(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
                outputPath = Path.Combine(outputDirectory, "Mock Track.mp3");
                await File.WriteAllBytesAsync(outputPath, [0x49, 0x44, 0x33], ct);
            }
        }

        return new YtDlpResult(
            Success: true,
            ExitCode: 0,
            Output: outputPath ?? "Completed mock run",
            Error: null);
    }

    public async Task<JsonElement> QueryMetadataAsync(
        string url,
        string? cookiesFilePath = null,
        bool isFlatPlaylist = true,
        CancellationToken ct = default,
        int? playlistEnd = null)
    {
        LastQueryPlaylistEnd = playlistEnd;
        if (QueryMetadataHandler != null)
        {
            return await QueryMetadataHandler(url, cookiesFilePath, isFlatPlaylist, ct);
        }

        // Return empty mock json
        using var doc = JsonDocument.Parse("{\"id\":\"test\",\"title\":\"Mock Track\",\"uploader\":\"Mock Artist\"}");
        return doc.RootElement.Clone();
    }
}
