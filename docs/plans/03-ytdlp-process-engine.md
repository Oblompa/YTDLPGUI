# Specialist Plan 03: yt-dlp Process Execution & Stream Engine

## Objective
Implement a high-performance, asynchronous process wrapper around `yt-dlp.exe` that controls execution, handles command-line arguments safely, parses real-time progress indicators from stdout, captures structured errors from stderr, and provides responsive cancellation and throttle control.

---

## 1. Process Spawning Specification

```csharp
var startInfo = new ProcessStartInfo
{
    FileName = ytDlpPath,
    Arguments = argumentString,
    UseShellExecute = false,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    CreateNoWindow = true,
    StandardOutputEncoding = Encoding.UTF8,
    StandardErrorEncoding = Encoding.UTF8
};
```

---

## 2. Interface Definition (`IYtDlpRunner`)

```csharp
namespace YtDlpAudio.Core.Services;

public record DownloadProgressUpdate(
    string VideoId,
    double Percent,
    string DownloadSpeed,
    string Eta,
    string TotalSize,
    DownloadState State
);

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

public interface ISearchService
{
    Task<IReadOnlyList<SearchResultItem>> SearchAsync(
        string query,
        SearchFilterType filter = SearchFilterType.Tracks,
        int maxResults = 20,
        CancellationToken ct = default
    );
}
```

---

## 3. Real-Time Output Parsing Rules

yt-dlp reports states and progress line-by-line via stdout. The engine uses compiled regular expressions:

### 3.1 Progress Pattern
```csharp
// Matches: [download]  45.2% of ~120.50MiB at 12.4MiB/s ETA 00:05
[GeneratedRegex(@"\[download\]\s+(\d+(?:\.\d+)?)%\s+of\s+~?([0-9\.]+[A-Za-z]+)\s+at\s+([0-9\.]+[A-Za-z]+/s)\s+ETA\s+(\d+:\d+)")]
private static partial Regex DownloadProgressRegex();
```

### 3.2 State Transition Patterns
* **Audio Extraction:** `[ExtractAudio] Destination: ...` -> `DownloadState.ExtractAudio`
* **Metadata Embedding:** `[Metadata] Adding metadata to ...` -> `DownloadState.Tagging`
* **Thumbnail Embedding:** `[Thumbnails:AtomicParsley]` or `[EmbedThumbnail]` -> `DownloadState.EmbeddingThumbnail`
* **Completion:** `[download] 100% of ...` or post-processing finishes -> `DownloadState.Completed`

---

## 4. Argument Builder & Sanitization

```csharp
public class YtDlpArgumentBuilder
{
    private readonly List<string> _args = new();

    public YtDlpArgumentBuilder WithFfmpegLocation(string path)
    {
        _args.Add($"--ffmpeg-location \"{path}\"");
        return this;
    }

    public YtDlpArgumentBuilder WithCookies(string? cookieFilePath)
    {
        if (!string.IsNullOrEmpty(cookieFilePath))
        {
            _args.Add($"--cookies \"{cookieFilePath}\"");
        }
        return this;
    }

    public YtDlpArgumentBuilder WithAudioOnly(string format = "mp3", string quality = "0")
    {
        _args.Add("-x");
        _args.Add($"--audio-format {format}");
        _args.Add($"--audio-quality {quality}");
        return this;
    }

    public YtDlpArgumentBuilder WithBestAudioStream(bool preferPremium = true)
    {
        // 141 is YouTube Premium 256k AAC. Falls back to bestaudio.
        _args.Add(preferPremium ? "-f \"ba[format_id=141]/ba\"" : "-f \"ba\"");
        return this;
    }

    public YtDlpArgumentBuilder WithEmbeddings()
    {
        _args.Add("--embed-metadata");
        _args.Add("--embed-thumbnail");
        _args.Add("--convert-thumbnails jpg");
        return this;
    }

    public YtDlpArgumentBuilder WithOutputTemplate(string template)
    {
        _args.Add($"-o \"{template}\"");
        return this;
    }

    public string Build(string url)
    {
        _args.Add($"\"{url}\"");
        return string.Join(" ", _args);
    }
}
```

---

## 5. Cancellation & Process Termination
* `yt-dlp` spawns `ffmpeg` as a child process when remuxing and extracting audio.
* Simply calling `process.Kill()` may leave orphan `ffmpeg.exe` processes holding file locks on partial `.mp3` files.
* **Solution:** Use process tree termination:
  ```csharp
  process.Kill(entireProcessTree: true);
  ```
* Register the cancellation token to trigger `entireProcessTree: true` immediately, followed by cleaning up `.part` and `.temp.*` files.

---

## 6. Download Failure Diagnostics (Release Blocker)

The user reports that every download fails while search and sign-in work. Treat this as a release blocker; investigate the actual failing stage instead of presuming the cause.

**Capture and surface**:
- yt-dlp exit code and stderr, including FFmpeg/postprocessor output needed to diagnose conversion and embedding failures.
- The failed stage and a concise actionable message on the queue item; retain full sanitized diagnostic detail in the application log.
- Relevant non-secret invocation context: yt-dlp and FFmpeg versions, output directory/writeability, selected format and audio arguments, and whether the command was guest or authenticated. Never log cookie contents, tokens, or credential-bearing values.
- Whether a non-empty output file was produced. Exit code zero alone is not proof that the requested MP3 is present and valid.

**Regression coverage**:
- Simulate source-extraction, transfer, FFmpeg-conversion, and metadata/postprocessor failures; verify exit codes and stderr reach the user-facing result.
- Verify successful execution yields the expected non-empty MP3 in the requested output directory.
- Exercise public downloads without cookies and authenticated downloads with temporary cookie files while confirming cleanup/redaction.
- Run a separately gated Windows E2E test against a source the tester is authorized to download; do not use copyrighted commercial music as a test fixture.

## 7. Resilient Playlist Metadata

- Metadata enumeration for a playlist uses yt-dlp's `--ignore-errors` so one unavailable entry does not discard the rest of the playlist response.
- Parse only entries with a usable video ID and title; skip unavailable, malformed, and empty entries.
- If playlist inspection fails, preserve the playlist queue row and surface the error so the user can retry or remove it.
- Playlist metadata enumeration must be lazy: search results contain playlist summaries only, and only an explicitly expanded/queued playlist is inspected.
