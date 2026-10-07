# YtDlpAudio: Windows Audio & MP3 Downloader for YouTube

A native Windows desktop application (built with C# / .NET 9 and WPF/WinUI) encapsulating `yt-dlp` and `ffmpeg` specifically tailored for downloading high-fidelity audio tracks, albums, and playlists.

## Key Capabilities
* **Zero-Config Toolchain:** Automatically downloads and updates `yt-dlp.exe`, `ffmpeg.exe`, and `ffprobe.exe` into an isolated application data directory without altering system environment variables.
* **YouTube Premium Integration:** In-app Microsoft Edge WebView2 authentication enables access to:
  * 256 kbps AAC high-bitrate streams (format `141`).
  * Private library playlists ("Liked Music", "Watch Later", custom private collections).
* **Audio-Centric Transcoding Pipeline:**
  * Re-encodes audio to MP3 using FFmpeg's `libmp3lame` (320kbps CBR, VBR V0, or 192kbps).
  * Automatically converts WebP cover thumbnails to baseline JPEG and embeds them into ID3v2 APIC tags.
  * Cleans YouTube-specific noise and clutter from song titles.
* **Interactive Playlist Manager:** Fast `--flat-playlist` metadata inspection with track checklists and live progress/ETA indicators.

---

## Architecture & Specialist Plans

The detailed implementation blueprints are divided by domain in `docs/plans/` for specialist AI models to implement independently:

| Plan File | Domain & Role | Primary Focus |
|---|---|---|
| [`docs/architecture/overview.md`](docs/architecture/overview.md) | **System Architect** | High-level architecture, contracts, and data flows. |
| [`docs/plans/01-dependency-provisioner.md`](docs/plans/01-dependency-provisioner.md) | **Toolchain Specialist** | Automated downloading, SHA-256 verification, and updates for `yt-dlp` and `ffmpeg`. |
| [`docs/plans/02-auth-and-cookies.md`](docs/plans/02-auth-and-cookies.md) | **Auth Specialist** | WebView2 YouTube login, cookie extraction, DPAPI encryption, and Premium validation. |
| [`docs/plans/03-ytdlp-process-engine.md`](docs/plans/03-ytdlp-process-engine.md) | **CLI Engine Specialist** | Async process execution, regex stdout stream parsing, cancellation, and argument construction. |
| [`docs/plans/04-audio-metadata-pipeline.md`](docs/plans/04-audio-metadata-pipeline.md) | **Audio/ID3 Specialist** | MP3 presets, WebP-to-JPEG album art conversion, title cleansing, and folder routing. |
| [`docs/plans/05-gui-and-viewmodels.md`](docs/plans/05-gui-and-viewmodels.md) | **UI/UX Specialist** | MVVM GUI implementation, tracklist selection cards, and real-time download queue. |
| [`docs/plans/06-integration-and-testing.md`](docs/plans/06-integration-and-testing.md) | **QA & Testing Specialist** | Unit tests for stream parsers, mock process runners, and test fixtures. |

---

## Directory Layout

```
YtDlpAudio/
├── docs/
│   ├── architecture/
│   └── plans/
├── src/
│   ├── YtDlpAudio.Core/             # Contracts, domain models, common records
│   ├── YtDlpAudio.Infrastructure/   # Toolchain, Auth, Process runner
│   └── YtDlpAudio.UI/               # WPF / WinUI 3 views, ViewModels
└── tests/
    └── YtDlpAudio.Core.Tests/       # Unit & integration tests
```
