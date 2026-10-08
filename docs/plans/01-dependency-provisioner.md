# Specialist Plan 01: Prerequisite & Dependency Provisioner

## Objective
Implement an autonomous prerequisite provisioner that ensures `yt-dlp.exe`, `ffmpeg.exe`, `ffprobe.exe`, and the Deno JavaScript runtime are present and verified in `%LOCALAPPDATA%\YtDlpAudio\bin\` without altering the Windows system `PATH`. Deno 2.3.0 or newer is required to solve YouTube's JavaScript challenges.

---

## 1. Upstream Binary Sources & Targets

### 1.1 yt-dlp
* **Source:** GitHub API: `https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest`
* **Direct Asset URL:** `https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe`
* **SHA-256 Checksum:** `https://github.com/yt-dlp/yt-dlp/releases/latest/download/SHA2-256SUMS`
* **Local Path:** `%LOCALAPPDATA%\YtDlpAudio\bin\yt-dlp.exe`

### 1.2 FFmpeg & FFprobe
* **Source:** `yt-dlp/FFmpeg-Builds` (optimized for yt-dlp compatibility)
* **API:** `https://api.github.com/repos/yt-dlp/FFmpeg-Builds/releases/latest`
* **Target Asset:** `ffmpeg-master-latest-win64-gpl.zip` (contains `ffmpeg.exe` and `ffprobe.exe` in `bin/`)
* **Local Extraction Paths:**
  * `%LOCALAPPDATA%\YtDlpAudio\bin\ffmpeg.exe`
  * `%LOCALAPPDATA%\YtDlpAudio\bin\ffprobe.exe`

### 1.3 Deno JavaScript Runtime
* **Source:** Official Deno GitHub releases
* **Direct Asset URL:** `https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip`
* **Minimum Version:** 2.3.0
* **Local Path:** `%LOCALAPPDATA%\YtDlpAudio\bin\deno.exe`
* **Purpose:** Required by yt-dlp to solve YouTube's JavaScript challenges and retrieve media formats.

---

## 2. Interface Definition (`IDependencyManager`)

```csharp
namespace YtDlpAudio.Core.Services;

public record DependencyStatus(
    bool YtDlpInstalled,
    string? YtDlpVersion,
    bool FFmpegInstalled,
    string? FFmpegVersion,
    bool DenoInstalled,
    string? DenoVersion
)
{
    public bool AllReady => YtDlpInstalled && FFmpegInstalled && DenoInstalled;
}

public record ProvisioningProgress(
    string CurrentStep,
    double PercentComplete,
    long BytesReceived,
    long? TotalBytes
);

public interface IDependencyManager
{
    string BinDirectory { get; }
    string YtDlpPath { get; }
    string FFmpegPath { get; }
    string FFprobePath { get; }
    string DenoPath { get; }

    Task<DependencyStatus> CheckStatusAsync(CancellationToken ct = default);
    Task<bool> ProvisionAllAsync(IProgress<ProvisioningProgress> progress, CancellationToken ct = default);
    Task<bool> UpdateYtDlpAsync(IProgress<ProvisioningProgress> progress, CancellationToken ct = default);
    Task<bool> EnsureDenoAsync(IProgress<ProvisioningProgress> progress, CancellationToken ct = default);
    Task<string?> GetYtDlpVersionAsync(CancellationToken ct = default);
}
```

---

## 3. Provisioning Workflow

```
[App Startup]
       │
       ▼
CheckStatusAsync()
       │
       ├─► Are yt-dlp.exe, ffmpeg.exe, ffprobe.exe, and deno.exe present in %LOCALAPPDATA%\YtDlpAudio\bin\?
       │   ├── YES ──► Quick validation (check execute permissions & version string)
       │   └── NO  ──► Emit Status: MissingDependencies
       │
       ▼
[If Missing or User Clicks "Install / Update"]
       │
       ├─► 1. Ensure directory exists: %LOCALAPPDATA%\YtDlpAudio\bin\
       │
       ├─► 2. Download yt-dlp.exe
       │      - Stream via HttpClient with Progress reporting
       │      - Fetch SHA2-256SUMS, compute SHA256 of downloaded file, verify match
       │      - Atomic write: download to yt-dlp.exe.tmp, rename to yt-dlp.exe
       │
       ├─► 3. Download Deno for Windows x64
       │      - Download the official latest release archive
       │      - Extract only deno.exe into the same binary directory
       │      - Verify the installed version is at least 2.3.0
       │
       ├─► 4. Download FFmpeg-Builds archive
       │      - Download ffmpeg-master-latest-win64-gpl.zip to temp folder
       │      - Extract only bin/ffmpeg.exe and bin/ffprobe.exe using System.IO.Compression.ZipFile
       │      - Move extracted binaries to %LOCALAPPDATA%\YtDlpAudio\bin\
       │      - Clean up temporary zip file
       │
       └─► 5. Verification Check
              - Execute `yt-dlp.exe --version`
              - Execute `ffmpeg.exe -version`
              - Execute `deno.exe --version`
              - Return ready status
```

---

## 4. Error Handling & Edge Cases
1. **GitHub API Rate Limiting (HTTP 403):**
   * Fall back to direct static download URLs (`/releases/latest/download/...`) without hitting the `/repos/.../releases` API.
2. **File Locked / Antivirus In-Use (SharingViolationException):**
   * Before replacing an existing binary, ensure no running `yt-dlp` or `ffmpeg` child processes exist.
   * Attempt atomic rename replacement (`File.Move(..., overwrite: true)`).
3. **Partial Downloads:**
   * Always download to `.tmp` files and verify SHA-256 or zip integrity before finalizing the rename.
