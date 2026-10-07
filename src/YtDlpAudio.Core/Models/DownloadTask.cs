namespace YtDlpAudio.Core.Models;

public enum DownloadState
{
    Queued,
    Analyzing,
    Downloading,
    ExtractingAudio,
    Tagging,
    EmbeddingThumbnail,
    Completed,
    Failed,
    Cancelled
}

public record DownloadProgressUpdate(
    string VideoId,
    double Percent,
    string DownloadSpeed,
    string Eta,
    string TotalSize,
    DownloadState State
);

public record YtDlpResult(
    bool Success,
    int ExitCode,
    string Output,
    string? Error
);
